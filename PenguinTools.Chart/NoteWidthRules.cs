using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

internal static class NoteWidthRules
{
    // Native curve clipping can collapse a nonjudging sample. It does not
    // permit a zero-width playable head, judgement or AIR attachment.
    internal static bool IsClippedControl(U.Note note) => note is
        U.SlideJoint { Joint: Joint.C, IsCurveControl: false } or
        U.AirSlideJoint { Joint: Joint.C, IsCurveControl: false };

    internal static void Validate(U.Chart chart) => Check(chart, Fail);

    internal static void Validate(C.Chart chart) => Check(chart, Fail);

    /// <summary>
    /// Widens unsupported widths in parsed input to one lane, as earlier releases did,
    /// and reports each change instead of rejecting the whole chart.
    /// </summary>
    /// <returns>Whether any width was changed.</returns>
    internal static bool Widen(U.Chart chart, IDiagnosticSink diagnostics)
    {
        var changed = false;
        Check(chart, (width, tick, _, widen) => changed |= Report(diagnostics, width, tick, widen));
        return changed;
    }

    /// <inheritdoc cref="Widen(U.Chart, IDiagnosticSink)"/>
    internal static bool Widen(C.Chart chart, IDiagnosticSink diagnostics)
    {
        var changed = false;
        Check(chart, (width, tick, _, widen) => changed |= Report(diagnostics, width, tick, widen));
        return changed;
    }

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

    private static void Check(C.Chart chart, Action<int, int, object, Action> invalid)
    {
        foreach (var note in chart.Notes)
        {
            if (note.Width <= 0 && !(note.Width == 0 && note is C.Slide or C.AirSlide))
            {
                invalid(note.Width, note.Tick.Original, note, () => note.Width = 1);
            }
            if (note is C.Slide or C.AirSlide or C.AirCrash)
            {
                var path = (C.LongNote)note;
                var control = note is C.Slide { Joint: Joint.C } or C.AirSlide { Joint: Joint.C };
                if (path.EndWidth <= 0 && !(path.EndWidth == 0 && control))
                {
                    invalid(path.EndWidth, path.EndTick.Original, note, () => path.EndWidth = 1);
                }
            }
            if (note is C.AirSlide { Width: 0 } air &&
                (air.Parent is not C.AirSlide { Joint: Joint.C, EndWidth: 0 } prior ||
                 prior.EndTick.Round != air.Tick.Round || prior.EndLane != air.Lane ||
                 C2sHeight.Key(prior.EndHeight) != C2sHeight.Key(air.Height)))
            {
                invalid(air.Width, air.Tick.Original, air, () => air.Width = 1);
            }
        }
        foreach (var path in C2sSlidePaths.Build(chart.Notes.OfType<C.Slide>()))
        {
            if (path[0].Width == 0)
            {
                var head = path[0];
                invalid(0, head.Tick.Original, head, () => head.Width = 1);
            }
        }
    }

    private static bool Report(IDiagnosticSink diagnostics, int width, int tick, Action widen)
    {
        widen();
        diagnostics.Report(new TimedDiagnostic(Severity.Warning, Msg.Create(MsgKeys.Warn_Note_width_adjusted, width), tick));
        return true;
    }

    private static void Fail(int width, int tick, object note, Action _) =>
        throw new TimedDiagnosticException(Msg.Create(MsgKeys.MgCrit_Invalid_note_width, width), tick, note);
}
