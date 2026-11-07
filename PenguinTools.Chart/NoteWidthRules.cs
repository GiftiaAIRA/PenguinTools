using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class NoteWidthRules
{
    // Native curve clipping can collapse a nonjudging sample. It does not
    // permit a zero-width playable head, judgement or AIR attachment.
    internal static bool IsClippedControl(U.Note note) => note is
        U.SlideJoint { Joint: Joint.C, IsCurveControl: false } or
        U.AirSlideJoint { Joint: Joint.C, IsCurveControl: false };

    internal static void Validate(U.Chart chart) => Check(chart, (width, tick, note, _) =>
        throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Invalid_note_width, width), tick, note));

    /// <summary>
    /// Widens unsupported widths in parsed MGXC or UGC input to one lane, as earlier
    /// releases did, and reports each change instead of rejecting the whole chart.
    /// </summary>
    internal static void Widen(U.Chart chart, IDiagnosticSink diagnostics) => Check(chart, (width, tick, _, widen) =>
    {
        widen();
        diagnostics.Report(new TimedDiagnostic(Severity.Warning, Msg.Create(MsgKeys.Warn_Note_width_adjusted, width), tick));
    });

    private static void Check(U.Chart chart, Action<int, int, object, Action> invalid)
    {
        foreach (var note in chart.Notes.Children.SelectMany(n => new[] { n }.Concat(n.Children)))
        {
            if (note.Width <= 0 && !(note.Width == 0 && IsClippedControl(note)) ||
                note.Width == 0 && note is U.PositiveNote { PairNote: not null })
            {
                invalid(note.Width, note.Tick.Original, note, () => note.Width = 1);
            }
        }
    }
}
