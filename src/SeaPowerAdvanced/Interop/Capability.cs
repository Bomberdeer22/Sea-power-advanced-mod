using System.Reflection;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// One game member the mod needs, plus what happened when it went looking.
///
/// This class was accidentally deleted in an earlier rewrite of <see cref="Capabilities"/>, which is
/// why the build failed with a dozen "type or namespace name 'Capability' could not be found" errors
/// in a file that looked perfectly fine. It lives in its own file now so a rewrite of the registry
/// cannot take it with it.
/// </summary>
internal sealed class Capability
{
    internal Capability(string id, string description)
    {
        Id = id;
        Description = description;
    }

    internal string Id { get; }

    internal string Description { get; }

    /// <summary>The member that was found, if any. Never used unless <see cref="Found"/>.</summary>
    internal MemberInfo? Member { get; set; }

    /// <summary>What else was tried, so a failure can be acted on without another report.</summary>
    internal string Tried { get; set; } = "";

    internal bool Found => Member != null;

    internal string Signature => MemberProbe.Describe(Member);
}
