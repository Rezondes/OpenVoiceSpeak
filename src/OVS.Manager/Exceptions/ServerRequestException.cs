using OVS.Manager.Servers;

namespace OVS.Manager.Exceptions;

/// <summary>Package M7: a server request the manager refuses, with the <see cref="Problem"/> the page shows.</summary>
internal sealed class ServerRequestException : Exception
{
    /// <summary>A refused request.</summary>
    public ServerRequestException() : base("The server request was refused.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public ServerRequestException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public ServerRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Refused for <paramref name="problem"/>.</summary>
    public ServerRequestException(ServerProblem problem) : base($"The server request was refused: {problem}.")
    {
        Problem = problem;
    }

    /// <summary>Refused for <paramref name="problem"/>, found through <paramref name="innerException"/>.</summary>
    public ServerRequestException(ServerProblem problem, Exception innerException) : base($"The server request was refused: {problem}.", innerException)
    {
        Problem = problem;
    }

    /// <summary>Why it was refused.</summary>
    public ServerProblem Problem { get; }
}
