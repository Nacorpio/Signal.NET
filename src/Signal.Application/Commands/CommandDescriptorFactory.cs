using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Commands.Preconditions;

namespace Signal.Application.Commands;

/// <summary>
/// Builds <see cref="CommandDescriptor"/>s via reflection once at startup. Invocations use compiled expression
/// delegates and a cached <see cref="ObjectFactory"/>, so executing a command does not use reflection.
/// </summary>
internal static class CommandDescriptorFactory
{
    /// <summary>Where the value of a method parameter comes from.</summary>
    private enum SlotKind
    {
        /// <summary>Bound from the message text.</summary>
        Bound,

        /// <summary>The <see cref="CommandContext"/>.</summary>
        Context,

        /// <summary>The message's <see cref="CancellationToken"/>.</summary>
        CancellationToken,
    }

    /// <summary>Describes a class-based command using the metadata of a resolved instance.</summary>
    /// <param name="commandType">The <see cref="ICommand"/> implementation (resolved from the message scope on execution).</param>
    /// <param name="instance">An instance used to read the metadata.</param>
    /// <returns>The descriptor.</returns>
    public static CommandDescriptor FromCommand(Type commandType, ICommand instance) => new(
        instance.Name,
        (context, _) => ((ICommand)context.Services.GetRequiredService(commandType)).ExecuteAsync(context, context.CancellationToken),
        commandType,
        instance.Aliases,
        instance.Description,
        instance.Usage,
        preconditions: commandType.GetCustomAttributes<PreconditionAttribute>(inherit: true),
        hidden: instance.Hidden);

    /// <summary>Describes every public <c>[Command]</c> method of a module.</summary>
    /// <param name="moduleType">A non-abstract <see cref="CommandModule"/>.</param>
    /// <returns>One descriptor per command method.</returns>
    /// <exception cref="ArgumentException">The type is not a non-abstract <see cref="CommandModule"/>.</exception>
    /// <exception cref="InvalidOperationException">A method has an invalid signature (e.g. misplaced <c>[Remainder]</c>).</exception>
    public static IEnumerable<CommandDescriptor> FromModule(Type moduleType)
    {
        if (!typeof(CommandModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
        {
            throw new ArgumentException($"{moduleType} must be a non-abstract {nameof(CommandModule)}.", nameof(moduleType));
        }

        var factory = ActivatorUtilities.CreateFactory(moduleType, Type.EmptyTypes);
        var modulePreconditions = moduleType.GetCustomAttributes<PreconditionAttribute>(inherit: true).ToArray();
        var nullability = new NullabilityInfoContext();

        foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<CommandAttribute>() is not { } attribute)
            {
                continue;
            }

            var (parameters, slots) = DescribeParameters(method, nullability);
            var invoker = CompileInvoker(method);

            // Creates the module from the message scope, fills injected parameters, invokes and disposes.
            async Task Execute(CommandContext context, object?[] bound)
            {
                var arguments = new object?[slots.Length];
                for (int i = 0, b = 0; i < slots.Length; i++)
                {
                    arguments[i] = slots[i] switch
                    {
                        SlotKind.Context => context,
                        SlotKind.CancellationToken => context.CancellationToken,
                        _ => bound[b++],
                    };
                }

                var module = (CommandModule)factory(context.Services, null);
                module.Context = context;
                try
                {
                    await invoker(module, arguments);
                }
                finally
                {
                    if (module is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else if (module is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            yield return new CommandDescriptor(
                attribute.Name,
                Execute,
                moduleType,
                attribute.Aliases,
                attribute.Description,
                attribute.Usage,
                parameters,
                [.. modulePreconditions, .. method.GetCustomAttributes<PreconditionAttribute>(inherit: true)],
                attribute.Hidden,
                method);
        }
    }

    /// <summary>Classifies the method parameters into injected slots and bindable <see cref="CommandParameter"/>s.</summary>
    private static (List<CommandParameter> Parameters, SlotKind[] Slots) DescribeParameters(MethodInfo method, NullabilityInfoContext nullability)
    {
        var infos = method.GetParameters();
        var slots = new SlotKind[infos.Length];
        var parameters = new List<CommandParameter>();

        for (var i = 0; i < infos.Length; i++)
        {
            var info = infos[i];
            if (info.ParameterType == typeof(CommandContext))
            {
                slots[i] = SlotKind.Context;
                continue;
            }

            if (info.ParameterType == typeof(CancellationToken))
            {
                slots[i] = SlotKind.CancellationToken;
                continue;
            }

            slots[i] = SlotKind.Bound;
            var type = info.ParameterType;
            var isNullable = Nullable.GetUnderlyingType(type) is not null
                || (!type.IsValueType && nullability.Create(info).WriteState == NullabilityState.Nullable);
            var flag = info.GetCustomAttribute<FlagAttribute>();
            var isSwitch = flag is not null && (type == typeof(bool) || type == typeof(bool?));
            var isRemainder = info.GetCustomAttribute<RemainderAttribute>() is not null;

            if (isRemainder && type != typeof(string))
            {
                throw new InvalidOperationException($"[Remainder] parameter '{info.Name}' of {method.DeclaringType}.{method.Name} must be a string.");
            }

            parameters.Add(new CommandParameter(
                info.Name ?? $"arg{i}",
                type,
                IsOptional: info.HasDefaultValue || isNullable || isSwitch,
                DefaultValue: DefaultValue(info, isSwitch),
                isRemainder,
                flag is null ? null : flag.Name ?? info.Name,
                info.GetCustomAttribute<SummaryAttribute>()?.Text));
        }

        var positional = parameters.Where(p => !p.IsFlag).ToList();
        if (positional.SkipLast(1).Any(p => p.IsRemainder))
        {
            throw new InvalidOperationException($"[Remainder] must be the last positional parameter of {method.DeclaringType}.{method.Name}.");
        }

        return (parameters, slots);
    }

    /// <summary>The value for an omitted argument; never <see langword="null"/> for non-nullable value types.</summary>
    private static object? DefaultValue(ParameterInfo info, bool isSwitch)
    {
        var type = info.ParameterType;
        if (info.HasDefaultValue && info.DefaultValue is not null)
        {
            return info.DefaultValue;
        }

        if (isSwitch && type == typeof(bool))
        {
            return false;
        }

        // `= default` on a struct, or no default at all on a non-nullable struct.
        return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
    }

    /// <summary>
    /// Compiles <c>(object target, object?[] args) =&gt; (Task)((TModule)target).Method((T0)args[0], …)</c>,
    /// normalising <see langword="void"/> and <see cref="ValueTask"/> return types to <see cref="Task"/>.
    /// </summary>
    private static Func<object, object?[], Task> CompileInvoker(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var args = Expression.Parameter(typeof(object?[]), "args");
        var call = Expression.Call(
            Expression.Convert(target, method.DeclaringType!),
            method,
            method.GetParameters().Select((p, i) =>
                Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType)));

        var returnType = method.ReturnType;
        Expression body;
        if (returnType == typeof(void))
        {
            body = Expression.Block(call, Expression.Constant(Task.CompletedTask));
        }
        else if (typeof(Task).IsAssignableFrom(returnType))
        {
            body = Expression.Convert(call, typeof(Task));
        }
        else if (returnType == typeof(ValueTask) || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            body = Expression.Convert(Expression.Call(call, returnType.GetMethod(nameof(ValueTask.AsTask))!), typeof(Task));
        }
        else
        {
            throw new InvalidOperationException(
                $"Command method {method.DeclaringType}.{method.Name} must return void, Task, Task<T>, ValueTask or ValueTask<T>.");
        }

        return Expression.Lambda<Func<object, object?[], Task>>(body, target, args).Compile();
    }
}
