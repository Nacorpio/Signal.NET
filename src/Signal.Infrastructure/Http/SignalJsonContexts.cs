using System.Text.Json;
using System.Text.Json.Serialization;
using Signal.Infrastructure.Http.Dtos;

namespace Signal.Infrastructure.Http;

/// <summary>
/// Source-generated (reflection-free, trimming/AOT friendly) serialization of the REST API DTOs.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>SnakeCaseLower</c> maps <c>Base64Attachments</c> to <c>base64_attachments</c>.</item>
/// <item><c>AllowReadingFromString</c> is needed because the API returns some numbers as strings (e.g. the send timestamp).</item>
/// <item><c>WhenWritingNull</c> omits unset optional fields so the API applies its defaults.</item>
/// </list>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorResponseDto))]
[JsonSerializable(typeof(AboutDto))]
[JsonSerializable(typeof(SendMessageRequestDto))]
[JsonSerializable(typeof(SendMessageResponseDto))]
[JsonSerializable(typeof(RegisterNumberRequestDto))]
[JsonSerializable(typeof(VerifyNumberRequestDto))]
[JsonSerializable(typeof(UnregisterNumberRequestDto))]
[JsonSerializable(typeof(RemoteDeleteRequestDto))]
[JsonSerializable(typeof(ReactionRequestDto))]
[JsonSerializable(typeof(ReceiptRequestDto))]
[JsonSerializable(typeof(TypingIndicatorRequestDto))]
[JsonSerializable(typeof(List<GroupDto>))]
[JsonSerializable(typeof(GroupDto))]
[JsonSerializable(typeof(CreateGroupRequestDto))]
[JsonSerializable(typeof(CreateGroupResponseDto))]
[JsonSerializable(typeof(UpdateGroupRequestDto))]
[JsonSerializable(typeof(GroupMembersRequestDto))]
[JsonSerializable(typeof(GroupAdminsRequestDto))]
[JsonSerializable(typeof(List<ContactDto>))]
[JsonSerializable(typeof(UpdateContactRequestDto))]
[JsonSerializable(typeof(List<IdentityDto>))]
[JsonSerializable(typeof(TrustIdentityRequestDto))]
[JsonSerializable(typeof(UpdateProfileRequestDto))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class SignalRestJsonContext : JsonSerializerContext;

/// <summary>
/// Source-generated serialization of received envelopes. signal-cli uses camelCase (web defaults), unlike the
/// snake_case REST endpoints, hence a separate context.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ReceivedMessageDto))]
[JsonSerializable(typeof(List<ReceivedMessageDto>))]
internal sealed partial class SignalEnvelopeJsonContext : JsonSerializerContext;
