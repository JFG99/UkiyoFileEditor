using System.Text.RegularExpressions;

namespace UkiyoFileEditor.TextProcessing;

/// <summary>Settings of the auto format.</summary>
/// <param name="SentencesOwnLine">Start every sentence on a new line, but only when the original does so too.</param>
/// <param name="ExtraLines">How many lines more than the original may be used (0 = exactly as many lines as the original).</param>
/// <param name="MaxWidthCap">No line may be wider than this (the line limit of the viewer settings); 0 = no limit. If the original's line count cannot respect it, more lines are used.</param>
public sealed record AutoFormatOptions(bool SentencesOwnLine, int ExtraLines, int MaxWidthCap, Regex Icon);
