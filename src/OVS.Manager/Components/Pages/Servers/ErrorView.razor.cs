using Microsoft.AspNetCore.Components;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M24 (AC1, AC2): one error with its ID, time, exception and whole stack trace.</summary>
sealed partial class ErrorView
{
    /// <summary>The log line.</summary>
    [Parameter, EditorRequired]
    public string Error { get; set; } = "";

    /// <summary>The error ID.</summary>
    [Parameter, EditorRequired]
    public string ErrorId { get; set; } = "";

    /// <summary>When, formatted.</summary>
    [Parameter, EditorRequired]
    public string At { get; set; } = "";

    /// <summary>The exception's message, "" without one.</summary>
    [Parameter]
    public string ExceptionMessage { get; set; } = "";

    /// <summary>The stack trace.</summary>
    [Parameter]
    public string StackTrace { get; set; } = "";
}
