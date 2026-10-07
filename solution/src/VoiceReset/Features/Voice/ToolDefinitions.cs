using Azure.AI.VoiceLive;

namespace VoiceReset.Features.Voice;

/// <summary>The eight tools. Only username and code are parameters (the backend checks them again); no IDs or free text.</summary>
public static class ToolDefinitions
{
    public const string StartRecovery = "start_recovery";
    public const string SubmitCode = "submit_code";
    public const string SendResetLink = "send_reset_link";
    public const string CheckResetStatus = "check_reset_status";
    public const string RequestHuman = "request_human";
    public const string ReportNoBrowser = "report_no_browser";
    public const string CancelReset = "cancel_reset";
    public const string EndCall = "end_call";

    private const string NoArguments = """{"type":"object","properties":{},"additionalProperties":false}""";

    public static IReadOnlyList<VoiceLiveFunctionDefinition> All { get; } =
    [
        Define(StartRecovery,
            "Start a password reset for the username the caller gave. Call it only after you read the username back and the caller said yes.",
            """{"type":"object","properties":{"username":{"type":"string","maxLength":64,"description":"The username the caller confirmed, in its written form, for example first.last."}},"required":["username"],"additionalProperties":false}"""),
        Define(SubmitCode,
            "Check the verification code the caller read from their recovery inbox. Call it only after you read the digits back and the caller said yes.",
            """{"type":"object","properties":{"code":{"type":"string","maxLength":16,"description":"The six-digit code the caller read back and confirmed, digits only."}},"required":["code"],"additionalProperties":false}"""),
        Define(SendResetLink,
            "Send the reset link to the caller's registered recovery inbox. Works only after the code was verified.", NoArguments),
        Define(CheckResetStatus,
            "Check whether the caller finished the reset in the browser form. Use it when the caller says they are done.", NoArguments),
        Define(RequestHuman,
            "Record an escalation for the help desk when the caller wants a person. It does not transfer the call.", NoArguments),
        Define(ReportNoBrowser,
            "Record an escalation for the help desk when the caller cannot use a browser to open the link. It does not transfer the call.", NoArguments),
        Define(CancelReset, "Cancel the password reset when the caller asks to stop.", NoArguments),
        Define(EndCall, "End the call when the conversation is finished. The system says goodbye.", NoArguments),
    ];

    private static VoiceLiveFunctionDefinition Define(string name, string description, string parametersJson) =>
        new(name) { Description = description, Parameters = BinaryData.FromString(parametersJson) };
}
