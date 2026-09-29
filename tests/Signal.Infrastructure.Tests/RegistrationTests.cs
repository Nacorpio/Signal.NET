using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Signal.Application.Abstractions;
using Signal.Domain.ValueObjects;

namespace Signal.Infrastructure.Tests;

public class RegistrationTests
{
    private static readonly PhoneNumber Number = PhoneNumber.Parse("+15550001111");

    private static (StubHandler Handler, ServiceProvider Provider) Build(HttpStatusCode status = HttpStatusCode.Created, int retries = 3)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.Created ? "" : """{"error":"Captcha required"}""") });
        return (handler, TestServices.Build(handler, o => o.Http.RetryCount = retries));
    }

    private static JsonElement Body((HttpMethod Method, string PathAndQuery, string? Body) request) =>
        JsonDocument.Parse(request.Body!).RootElement;

    [Fact]
    public async Task Register_defaults_to_sms_without_captcha()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IRegistrationService>().RegisterAsync(Number);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/register/%2B15550001111", request.PathAndQuery);
        Assert.Equal("{}", request.Body);
    }

    [Fact]
    public async Task Register_sends_voice_and_trimmed_captcha()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IRegistrationService>().RegisterAsync(
            Number, new RegistrationOptions(UseVoice: true, Captcha: "  signalcaptcha://token  "));

        var body = Body(Assert.Single(handler.Requests));
        Assert.True(body.GetProperty("use_voice").GetBoolean());
        Assert.Equal("signalcaptcha://token", body.GetProperty("captcha").GetString());
    }

    [Fact]
    public async Task Register_is_never_retried_and_surfaces_the_api_error()
    {
        var (handler, provider) = Build(HttpStatusCode.ServiceUnavailable, retries: 3);
        await using var _ = provider;

        var error = await Assert.ThrowsAsync<SignalApiException>(() =>
            provider.GetRequiredService<IRegistrationService>().RegisterAsync(Number));

        Assert.Equal("Captcha required", error.ApiError);
        Assert.Single(handler.Requests); // a retry would request a second verification code
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    public async Task Verify_normalizes_the_code_and_sends_the_pin(string code, string expected)
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IRegistrationService>().VerifyAsync(Number, code, pin: "1234");

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/v1/register/%2B15550001111/verify/{expected}", request.PathAndQuery);
        Assert.Equal("1234", Body(request).GetProperty("pin").GetString());
    }

    [Fact]
    public async Task Verify_without_pin_sends_empty_body()
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await provider.GetRequiredService<IRegistrationService>().VerifyAsync(Number, "123456");

        Assert.Equal("{}", Assert.Single(handler.Requests).Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" - ")]
    public async Task Verify_rejects_empty_codes_without_calling_the_api(string code)
    {
        var (handler, provider) = Build();
        await using var _ = provider;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<IRegistrationService>().VerifyAsync(Number, code));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unregister_sends_both_flags_explicitly()
    {
        var (handler, provider) = Build(HttpStatusCode.NoContent);
        await using var _ = provider;

        await provider.GetRequiredService<IRegistrationService>().UnregisterAsync(Number, deleteLocalData: true);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/unregister/%2B15550001111", request.PathAndQuery);
        var body = Body(request);
        Assert.False(body.GetProperty("delete_account").GetBoolean());
        Assert.True(body.GetProperty("delete_local_data").GetBoolean());
    }
}
