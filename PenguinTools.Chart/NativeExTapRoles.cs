using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart;

// Margrete transports native copyright across MGXC and UGC. Its bookmark and
// note encodings are format-specific, so only ambiguous independent EX taps
// need this separate, compact record.
internal static class NativeExTapRoles
{
    internal const string Prefix = "PT_EXROLE_V1=";
    private const int DigestLength = 16;

    internal static string Capture(IEnumerable<U.ExTapableNote> heads, IEnumerable<U.ExTap> taps)
    {
        using var payload = new MemoryStream();
        foreach (var (tap, key) in Keys(heads, taps))
        {
            if (tap.Role == U.ExTapRole.Explicit)
            {
                payload.Write(key);
            }
        }
        return payload.Length == 0 ? "" : Prefix + Convert.ToBase64String(payload.ToArray()) + ";";
    }

    internal static bool Restore(U.Chart chart, string copyright)
    {
        if (!copyright.Contains(Prefix, StringComparison.Ordinal))
        {
            return true;
        }
        var tokens = copyright.Split(';').Select(token => token.Trim())
            .Where(token => token.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        if (tokens.Length == 0)
        {
            return true;
        }
        if (tokens.Length != 1 || tokens[0].Length >= ushort.MaxValue)
        {
            return false;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(tokens[0][Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (payload.Length == 0 || payload.Length % DigestLength != 0)
        {
            return false;
        }

        var pending = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < payload.Length; offset += DigestLength)
        {
            if (!pending.Add(Convert.ToHexString(payload.AsSpan(offset, DigestLength))))
            {
                return false;
            }
        }

        var keys = Keys(chart.Notes.Children.OfType<U.ExTapableNote>(), chart.Notes.Children.OfType<U.ExTap>());
        var matched = true;
        foreach (var (tap, key) in keys)
        {
            if (!pending.Remove(Convert.ToHexString(key)))
            {
                continue;
            }
            if (tap.Role == U.ExTapRole.Auto)
            {
                tap.Role = U.ExTapRole.Explicit;
            }
            else if (tap.Role != U.ExTapRole.Explicit)
            {
                matched = false;
            }
        }
        return matched && pending.Count == 0;
    }

    private static IEnumerable<(U.ExTap Tap, byte[] Key)> Keys(IEnumerable<U.ExTapableNote> heads,
        IEnumerable<U.ExTap> taps)
    {
        var byTick = heads.ToLookup(note => note.Tick.Original);
        foreach (var group in taps.GroupBy(note => note.Tick.Original))
        {
            var longHeads = byTick[group.Key].Where(head => group.Any(tap => Covers(tap, head))).ToArray();
            if (longHeads.Length == 0)
            {
                continue;
            }
            var nativeTaps = group.Select(tap => (Tap: tap, Key: TapKey(tap))).ToArray();
            var context = string.Join('\n', longHeads.Select(PathKey).Order(StringComparer.Ordinal)) + "\n" +
                string.Join('\n', nativeTaps.Select(item => item.Key).Order(StringComparer.Ordinal));
            var duplicates = nativeTaps.GroupBy(item => item.Key, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            var ordered = duplicates.Count == 0 ? "" : string.Join('\n', nativeTaps.Select(item => item.Key));
            for (var i = 0; i < nativeTaps.Length; i++)
            {
                var (tap, key) = nativeTaps[i];
                if (longHeads.Any(head => Covers(tap, head)))
                {
                    // Distinguish identical EX taps by their order among the
                    // other EX taps at this tick, since that order can decide
                    // which carrier paints the long head.
                    var identity = duplicates.Contains(key)
                        ? string.Create(CultureInfo.InvariantCulture, $"{context}\n{ordered}\n{i}")
                        : context + "\n" + key;
                    yield return (tap, SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..DigestLength]);
                }
            }
        }
    }

    private static bool Covers(U.ExTap tap, U.ExTapableNote head) =>
        tap.Lane <= head.Lane && tap.Lane + (long)tap.Width >= head.Lane + (long)head.Width;

    private static string TapKey(U.ExTap tap) => string.Create(CultureInfo.InvariantCulture,
        $"X:{Position(tap)},{(int)tap.Effect},{AttachmentKey(tap.PairNote)}");

    private static string AttachmentKey(U.NegativeNote? pair) => pair switch
    {
        U.Air air => string.Create(CultureInfo.InvariantCulture, $"{PathKey(air)},{(int)air.Direction}"),
        U.AirHold or U.AirSlide => PathKey(pair),
        _ => "-"
    };

    private static string PathKey(U.Note note) => note.GetType().Name + ":" + Position(note) + ":" +
        string.Join(';', note.Children.Select(child => Position(child) + JointKey(child)));

    private static string JointKey(U.Note child) => child switch
    {
        U.SlideJoint { IsCurveControl: true } or U.AirSlideJoint { IsCurveControl: true } => ",B",
        U.SlideJoint { Joint: Models.Joint.C } or U.AirHoldJoint { Joint: Models.Joint.C } or
            U.AirSlideJoint { Joint: Models.Joint.C } => ",C",
        U.SlideJoint or U.AirHoldJoint or U.AirSlideJoint => ",D",
        _ => ""
    };

    private static string Position(U.Note note) => string.Create(CultureInfo.InvariantCulture,
        $"{note.Tick.Original},{note.Lane},{note.Width},{note.Timeline}");
}
