using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Signal.Application.Commands.Binding;
using Signal.Application.Commands.Parsing;
using Signal.Application.Configuration;
using Signal.Domain.Messaging;
using Signal.Domain.ValueObjects;

namespace Signal.Application.Background;

/// <summary>How a prompt ended.</summary>
public enum PromptStatus
{
    /// <summary>The sender answered with a valid value.</summary>
    Answered,

    /// <summary>No answer arrived in time (or a newer prompt to the same sender replaced this one).</summary>
    TimedOut,

    /// <summary>Every attempt was answered with a value that could not be parsed.</summary>
    Invalid,
}

/// <summary>The outcome of <see cref="BackgroundWork.PromptAsync{T}"/>.</summary>
/// <typeparam name="T">The requested type.</typeparam>
/// <param name="Status">How the prompt ended.</param>
/// <param name="Value">The parsed answer when <see cref="Status"/> is <see cref="PromptStatus.Answered"/>.</param>
/// <param name="Text">The last answer text, if any.</param>
public sealed record PromptResult<T>(PromptStatus Status, T? Value, string? Text)
{
    /// <summary>Whether the sender answered with a valid value.</summary>
    public bool IsAnswered => Status == PromptStatus.Answered;
}

/// <summary>
/// Pending prompts waiting for a sender's next message. The host passes every received envelope to
/// <see cref="TryDeliver"/> <b>before</b> routing it to a conversation partition, so an answer never waits behind
/// the work that asked for it.
/// </summary>
public interface IPromptRegistry
{
    /// <summary>
    /// Hands a received text message to the prompt waiting for its sender in its conversation, if any. Messages that
    /// parse as a command are never consumed, so commands keep working while a prompt waits.
    /// </summary>
    /// <param name="envelope">The received envelope.</param>
    /// <returns><see langword="true"/> if a prompt consumed the message; it must then not be processed further.</returns>
    bool TryDeliver(IncomingEnvelope envelope);

    /// <summary>
    /// Waits for the next text message of <paramref name="sender"/> in <paramref name="conversation"/>. The prompt is
    /// registered before this method returns its task, so call it <b>before</b> sending the question.
    /// </summary>
    /// <param name="account">The receiving account.</param>
    /// <param name="conversation">The conversation.</param>
    /// <param name="sender">The sender whose answer counts (in groups, other members are ignored).</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The answering envelope, or <see langword="null"/> on timeout or when a newer prompt replaced this one.</returns>
    Task<IncomingEnvelope?> WaitAsync(PhoneNumber account, Recipient conversation, Sender sender, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>In-memory <see cref="IPromptRegistry"/>. A sender is keyed by UUID and phone number, whichever are known.</summary>
internal sealed class PromptRegistry(ICommandParser parser) : IPromptRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IncomingEnvelope?>> _pending = new(StringComparer.Ordinal);

    public bool TryDeliver(IncomingEnvelope envelope)
    {
        if (envelope.Data is not { Text: { } text, Reaction: null } || parser.TryParse(text, out _))
        {
            return false;
        }

        foreach (var key in Keys(envelope.Account, envelope.Conversation, envelope.Source))
        {
            if (_pending.TryRemove(key, out var waiter) && waiter.TrySetResult(envelope))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IncomingEnvelope?> WaitAsync(PhoneNumber account, Recipient conversation, Sender sender, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<IncomingEnvelope?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = Keys(account, conversation, sender).ToList();
        foreach (var key in keys)
        {
            // A newer prompt to the same sender in the same conversation replaces the older one.
            if (_pending.TryGetValue(key, out var previous) && !ReferenceEquals(previous, waiter))
            {
                previous.TrySetResult(null);
            }

            _pending[key] = waiter;
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            foreach (var key in keys)
            {
                _pending.TryRemove(new KeyValuePair<string, TaskCompletionSource<IncomingEnvelope?>>(key, waiter));
            }
        }
    }

    private static IEnumerable<string> Keys(PhoneNumber account, Recipient conversation, Sender sender)
    {
        var prefix = $"{account.Value}|{conversation.Address}|";
        if (sender.Uuid is { } uuid)
        {
            yield return prefix + uuid.ToString("D");
        }

        if (sender.Number is { } number)
        {
            yield return prefix + number.Value;
        }
    }
}

public sealed partial class BackgroundWork
{
    /// <summary>Asks <see cref="Sender"/> a question and waits for their next text message in <see cref="Conversation"/>.</summary>
    /// <param name="question">The question to send.</param>
    /// <param name="timeout">How long to wait; defaults to <c>Signal:Background:PromptTimeout</c>.</param>
    /// <returns>The answer, or <see cref="PromptStatus.TimedOut"/>.</returns>
    /// <exception cref="InvalidOperationException">The work was not queued from a message, so there is no sender to ask.</exception>
    public async Task<PromptResult<string>> PromptAsync(string question, TimeSpan? timeout = null)
    {
        var answer = await AskAsync(question, timeout);
        return answer is null
            ? new PromptResult<string>(PromptStatus.TimedOut, null, null)
            : new PromptResult<string>(PromptStatus.Answered, answer, answer);
    }

    /// <summary>
    /// Asks <see cref="Sender"/> for a value of type <typeparamref name="T"/> (any <see cref="IParsable{TSelf}"/>: numbers,
    /// dates, <see cref="PhoneNumber"/>, …, parsed with the invariant culture). Invalid answers are rejected with a hint
    /// and the question is asked again, up to <paramref name="attempts"/> times.
    /// </summary>
    /// <typeparam name="T">The requested type.</typeparam>
    /// <param name="question">The question to send.</param>
    /// <param name="timeout">How long to wait for each answer; defaults to <c>Signal:Background:PromptTimeout</c>.</param>
    /// <param name="attempts">How many answers to accept before giving up with <see cref="PromptStatus.Invalid"/>.</param>
    /// <returns>The parsed answer, or why there is none.</returns>
    /// <exception cref="InvalidOperationException">The work was not queued from a message, so there is no sender to ask.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/> is less than 1.</exception>
    public async Task<PromptResult<T>> PromptAsync<T>(string question, TimeSpan? timeout = null, int attempts = 3)
        where T : IParsable<T>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        string? answer = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            answer = await AskAsync(attempt == 1 ? question : $"'{answer}' is not a valid {DisplayName<T>()}. {question}", timeout);
            if (answer is null)
            {
                return new PromptResult<T>(PromptStatus.TimedOut, default, null);
            }

            if (T.TryParse(answer.Trim(), CultureInfo.InvariantCulture, out var value))
            {
                return new PromptResult<T>(PromptStatus.Answered, value, answer);
            }
        }

        return new PromptResult<T>(PromptStatus.Invalid, default, answer);
    }

    private async Task<string?> AskAsync(string question, TimeSpan? timeout)
    {
        var sender = Sender ?? throw new InvalidOperationException("Prompts need a sender; queue the work from a message.");
        var wait = timeout ?? Services.GetRequiredService<IOptions<SignalOptions>>().Value.Background.PromptTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(wait, TimeSpan.Zero, nameof(timeout));

        // Register before asking: an answer that arrives right after the question must find the prompt waiting.
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var answer = Services.GetRequiredService<IPromptRegistry>().WaitAsync(Account, Conversation, sender, wait, cancel.Token);
        try
        {
            await ReplyAsync(question);
        }
        catch
        {
            await cancel.CancelAsync();
            await ((Task)answer).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        return (await answer)?.Data?.Text;
    }

    /// <summary>The same wording as argument errors ("whole number", "phone number …"), or the type name.</summary>
    private string DisplayName<T>() =>
        Services.GetService<IArgumentConverterProvider>() is { } provider && provider.TryGetConverter(typeof(T), out var converter)
            ? converter.DisplayName
            : typeof(T).Name;
}
