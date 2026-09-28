using Signal.Application.Abstractions;
using Signal.Application.Commands;
using Signal.Application.Commands.Preconditions;
using Signal.Domain.ValueObjects;

namespace Signal.Sample.Bot.Commands;

/// <summary>Group commands. Module-level preconditions apply to every command; services come from constructor injection.</summary>
[RequireGroup]
public sealed class GroupModule(IGroupService groups) : CommandModule
{
    [Command("groupinfo", Description = "Shows information about this group.")]
    public async Task InfoAsync(CancellationToken cancellationToken)
    {
        var group = await groups.GetAsync(Context.Account, Context.Group!.Value, cancellationToken);
        if (group is null)
        {
            await ReplyAsync("I could not load this group.");
            return;
        }

        await ReplyAsync($"{group.Name}\nMembers: {group.Members.Count}\nAdmins: {group.Admins.Count}\n{group.Description}".TrimEnd());
    }

    [Command("kick", Description = "Removes a member from this group.", Hidden = true)]
    [RequireAdmin]
    [RequireGroupAdmin]
    public async Task KickAsync([Summary("Phone number of the member")] PhoneNumber member, CancellationToken cancellationToken)
    {
        await groups.RemoveMembersAsync(Context.Account, Context.Group!.Value, [member.Value], cancellationToken);
        await ReplyAsync($"Removed {member}.");
    }
}
