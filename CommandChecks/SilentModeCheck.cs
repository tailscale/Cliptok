namespace Cliptok.CommandChecks
{
    public class SilentModeAttribute : ContextCheckAttribute;

    public class SilentModeCheck : IContextCheck<SilentModeAttribute>
    {
        public async ValueTask<string?> ExecuteCheckAsync(SilentModeAttribute attribute, CommandContext ctx)
        {
            if (ctx.Member == null)
                return null;

            return (Program.cfgjson.SilentMode && (await GetPermLevelAsync(ctx.Member) < ServerPermLevel.TrialModerator))
                ? "Silent mode is enabled and user is not a moderator"
                : null;
        }
    }
}
