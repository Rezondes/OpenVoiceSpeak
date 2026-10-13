using System.Text.RegularExpressions;

namespace OVS.Tests;

sealed record PlanParse(List<PlanReference> References, List<(int Line, string Span)> Ignored);
