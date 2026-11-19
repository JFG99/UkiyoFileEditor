namespace UkiyoFileEditor.Models;

/// <summary>A pack together with the result of reading it.</summary>
public sealed record LoadedPack(PackFile File, MessagePack? Pack, string? Error);
