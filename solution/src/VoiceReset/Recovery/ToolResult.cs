namespace VoiceReset.Recovery;

/// <summary>Result of a tool call. Say is the exact sentence the agent must use.</summary>
public sealed record ToolResult(bool Ok, string Status, string Say);
