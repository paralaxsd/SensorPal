partial class ThisAssembly
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    static string? _gitCommitUrl;
    static string? _repositoryUrl;

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    public static string GitCommitUrl => _gitCommitUrl ??= $"{RepositoryUrl}/commit/{GitCommitId}";

    public static string RepositoryUrl => _repositoryUrl ??= GetRepositoryUrl();

    public static string GitCommitIdShort
        => GitCommitId.Length > 7 ? GitCommitId[..7] : GitCommitId;

    public static string AssemblyShortFileVersion
        => Version.Parse(AssemblyFileVersion).ToString(3);

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    static string GetRepositoryUrl()
    {
        var url = GitRepositoryUrl;

        // Normalize SSH remote (git@github.com:user/repo.git) to HTTPS
        if (url.StartsWith("git@"))
            url = "https://" + url[4..].Replace(":", "/");

        // Strip trailing .git
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];

        return url;
    }
}
