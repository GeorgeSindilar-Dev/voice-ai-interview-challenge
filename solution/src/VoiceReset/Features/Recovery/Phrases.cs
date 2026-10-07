namespace VoiceReset.Features.Recovery;

/// <summary>The only sentences the agent may say about the reset. The same words for every username.</summary>
public static class Phrases
{
    public const string CodeSent = "If that account is enrolled, a verification code has been sent to its recovery inbox. It's valid for two minutes.";
    public const string UsernameUnclear = "I didn't catch a valid username. Please say it again, for example: first name dot last name.";
    public const string CantStartNow = "I can't start a reset for that username right now. Please try again in a few minutes.";
    public const string CodeUnclear = "I need all six digits of the code. Please read the whole code again.";
    public const string Verified = "Thanks, the code is verified. I can now send a password reset link to the same recovery inbox.";
    public const string CodeIncorrect = "That code wasn't correct. You have one try left.";
    public const string Exhausted = "That code wasn't correct either, so this reset attempt is now locked.";
    public const string Expired = "The verification code has expired, so I can't continue this reset.";
    public const string LinkSent = "I've sent a password reset link to the same recovery inbox. It's valid for 10 minutes, and it stays valid until it expires. Open it and choose your new password in the form; please don't tell me the password.";
    public const string Completed = "Your password has been reset.";
    public const string CompletedWithUnlock = "Your password has been reset and your account is unlocked.";
    public const string CantConfirmYet = "I can't confirm the reset yet. When you've submitted the form, ask me to check again.";
    public const string ResetFailed = "The reset didn't complete.";
    public const string LinkExpired = "The reset link expired before a reset was completed.";
    public const string HumanRequested = "I can't transfer you to a person.";
    public const string NoBrowser = "I can't finish the reset without a browser.";
    public const string TicketCreated = "A help-desk ticket was created; no one has joined this call.";
    public const string TicketNotCreated = "I couldn't create a help-desk ticket, and no one has joined this call. Please contact your help desk directly.";
    public const string Cancelled = "OK, I've cancelled this reset.";
    public const string CancelledAfterLink = "OK, I've stopped here. The link already sent can't be withdrawn; it stays valid until it expires.";
    public const string NotAvailableNow = "The reset service isn't responding right now. Please try again in a moment.";
    public const string NotAllowed = "I can't do that at this step of the reset.";
    public const string InvalidArgument = "Sorry, I didn't get that. Could you say it again?";
    public const string Goodbye = "Thank you for calling. Goodbye.";
}
