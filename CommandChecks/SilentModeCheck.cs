namespace Cliptok.CommandChecks
{
    public class SilentModeCheckAttribute : ContextCheckAttribute;

    public class SilentModeCheck : IContextCheck<SilentModeCheckAttribute>
    {
        public async ValueTask<string?> ExecuteCheckAsync(SilentModeCheckAttribute attribute, CommandContext ctx)
        {
            if (ctx.Member == null)
                return null;

            return (Program.cfgjson.SilentMode && (await GetPermLevelAsync(ctx.Member) < ServerPermLevel.TrialModerator))
                ? "Silent mode is enabled and user is not a moderator"
                : null;
        }
    }
}
