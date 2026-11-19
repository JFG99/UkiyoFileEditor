using System.Text.RegularExpressions;

namespace UkiyoFileEditor.Validation;

public sealed record Issue(Severity Severity, string Message);
