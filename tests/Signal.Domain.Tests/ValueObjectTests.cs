using Signal.Domain;
using Signal.Domain.Exceptions;
using Signal.Domain.ValueObjects;

namespace Signal.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+4915112345678", "+4915112345678")]
    [InlineData(" +49 151 1234-5678 ", "+4915112345678")]
    [InlineData("+1 (555) 000-1111", "+15550001111")]
    [InlineData("004915112345678", "+4915112345678")]
    public void Parses_and_normalizes(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Parse(input).Value);

    [Theory]
    [InlineData("")]
    [InlineData("4915112345678")]
    [InlineData("+0123456789")]
    [InlineData("+49abc")]
    [InlineData("+1234")]
    public void Rejects_invalid_numbers(string input)
    {
        Assert.False(PhoneNumber.TryParse(input, out _));
        Assert.Throws<InvalidPhoneNumberException>(() => PhoneNumber.Parse(input));
    }

    [Fact]
    public void Has_value_equality() => Assert.Equal(PhoneNumber.Parse("+49 151 12345678"), PhoneNumber.Parse("+4915112345678"));
}

public class GroupIdTests
{
    [Fact]
    public void Converts_internal_id_to_rest_id_and_back()
    {
        var id = GroupId.FromInternalId("abc123==");
        Assert.Equal("group.YWJjMTIzPT0=", id.Value);
        Assert.Equal("abc123==", id.InternalId);
    }

    [Theory]
    [InlineData("group.")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Rejects_invalid_ids(string? value) => Assert.False(GroupId.TryParse(value, out _));
}

public class RecipientTests
{
    [Theory]
    [InlineData("+4915112345678", typeof(PhoneNumber), "+4915112345678")]
    [InlineData("group.YWJj", typeof(GroupId), "group.YWJj")]
    [InlineData("8F2B0D6E-5C1A-4B7E-9A0F-2D3C4B5A6E7F", typeof(AccountId), "8f2b0d6e-5c1a-4b7e-9a0f-2d3c4b5a6e7f")]
    [InlineData(" alice.42 ", typeof(Username), "alice.42")]
    public void Detects_case_and_normalizes_address(string value, Type caseType, string address)
    {
        var recipient = Recipient.Parse(value);

        Assert.IsType(caseType, recipient.Value);
        Assert.Equal(address, recipient.Address);
    }

    [Fact]
    public void Converts_implicitly_from_every_case_type()
    {
        Recipient fromNumber = PhoneNumber.Parse("+4915112345678");
        Recipient fromAccount = new AccountId(Guid.Empty);
        Recipient fromUsername = Username.Parse("bob.1");
        Recipient fromGroup = GroupId.FromInternalId("x");

        Assert.IsType<PhoneNumber>(fromNumber.Value);
        Assert.Equal("00000000-0000-0000-0000-000000000000", fromAccount.Address);
        Assert.Equal("bob.1", fromUsername.ToString());
        Assert.True(fromGroup.IsGroup);
        Assert.False(fromNumber.IsGroup);
    }

    [Fact]
    public void Pattern_matching_is_exhaustive_over_case_types()
    {
        static string Describe(Recipient recipient) => recipient switch
        {
            PhoneNumber n => $"phone {n}",
            AccountId id => $"account {id}",
            Username u => $"user {u}",
            GroupId g => $"group {g}",
            null => "nobody",
        };

        Assert.Equal("user bob.1", Describe(Username.Parse("bob.1")));
        Assert.Equal("nobody", Describe(default));
    }

    [Fact]
    public void Has_value_equality() =>
        Assert.Equal<Recipient>(PhoneNumber.Parse("+49 151 12345678"), Recipient.Parse("+4915112345678"));

    [Fact]
    public void Default_recipient_has_no_address()
    {
        Recipient empty = default;

        Assert.Null(empty.Value);
        Assert.Equal(string.Empty, empty.ToString());
        Assert.Throws<InvalidOperationException>(() => empty.Address);
    }

    [Fact]
    public void Rejects_usernames_with_whitespace()
    {
        Assert.Throws<InvalidRecipientException>(() => Username.Parse("two words"));
        Assert.False(Recipient.TryParse("two words", out _));
        Assert.Throws<InvalidRecipientException>(() => Recipient.Parse(" "));
    }
}

public class ExecutionModeTests
{
    [Theory]
    [InlineData(ExecutionMode.Normal, false, false, "normal")]
    [InlineData(ExecutionMode.Native, false, true, "native")]
    [InlineData(ExecutionMode.JsonRpc, true, false, "json-rpc")]
    [InlineData(ExecutionMode.JsonRpcNative, true, true, "json-rpc-native")]
    public void Describes_transport_and_container_value(ExecutionMode mode, bool streaming, bool native, string containerValue)
    {
        Assert.Equal(streaming, mode.IsStreaming);
        Assert.Equal(native, mode.IsNative);
        Assert.Equal(containerValue, mode.ContainerValue);
        Assert.True(ExecutionModeExtensions.TryParseContainerValue(containerValue, out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Fact]
    public void Rejects_unknown_container_values() => Assert.False(ExecutionModeExtensions.TryParseContainerValue("turbo", out _));
}
