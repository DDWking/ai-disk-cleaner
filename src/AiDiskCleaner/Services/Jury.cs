namespace AiDiskCleaner.Services;

public sealed class JurySeat
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public AiProviderCfg Provider { get; init; } = null!;
    public string Model { get; init; } = "";
}

public sealed class JuryPick
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool On { get; set; }
}

public sealed class JuryGroup
{
    public string Name { get; set; } = "";
    public List<JuryPick> Models { get; set; } = new();
}

public sealed class VoteItem
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public string Note { get; set; } = "";
    public int Votes { get; set; }
    public List<string> Voters { get; } = new();
    public string Grade { get; set; } = "";
}

public static class Jury
{
    public static List<JurySeat> Seats()
    {
        var s = App.Settings;
        s.Migrate();
        var list = new List<JurySeat>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!s.AiJuryOn)
        {
            var p = s.CurrentProvider();
            string model = (s.AiModel ?? "").Trim();
            if (p != null && !string.IsNullOrEmpty(model))
                list.Add(new JurySeat { Id = p.Id + "|" + model, Label = model, Provider = p, Model = model });
            return list;
        }
        foreach (var id in s.AiJury)
        {
            var seat = Parse(id);
            if (seat == null) continue;
            string key = seat.Provider.Id + "|" + seat.Model;
            if (!seen.Add(key)) continue;
            list.Add(seat);
        }
        if (list.Count == 0)
        {
            var p = s.CurrentProvider();
            string model = (s.AiModel ?? "").Trim();
            if (p != null && !string.IsNullOrEmpty(model))
                list.Add(new JurySeat { Id = p.Id + "|" + model, Label = model, Provider = p, Model = model });
        }
        return list.Take(4).ToList();
    }

    public static string SeatId(AiProviderCfg p, string model) => p.Id + "|" + model;

    public static JurySeat? Parse(string id)
    {
        int i = id.IndexOf('|');
        if (i <= 0) return null;
        string pid = id[..i];
        string model = id[(i + 1)..].Trim();
        var p = App.Settings.AiProviders.FirstOrDefault(x => x.Id == pid);
        if (p == null || string.IsNullOrEmpty(model)) return null;
        string label = string.IsNullOrEmpty(p.Name) ? model : p.Name + " / " + model;
        return new JurySeat { Id = id, Label = label, Provider = p, Model = model };
    }

    public static List<VoteItem> Tally(IReadOnlyList<(JurySeat Seat, string Text)> replies)
    {
        var map = new Dictionary<string, VoteItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var (seat, text) in replies)
        {
            foreach (var part in ChatFormat.Parse(text))
            {
                if (part.Kind != ChatPartKind.Path || string.IsNullOrEmpty(part.Path)) continue;
                if (part.Section == Loc.SecKeep || part.Section == Loc.SecFolders) continue;
                string key = Norm(part.Path);
                if (key.Length < 4 || Protected(key)) continue;
                if (!map.TryGetValue(key, out var item))
                {
                    item = new VoteItem
                    {
                        Path = key,
                        Name = Short(key),
                        Size = SizeOf(part.Text),
                        Note = part.Note ?? "",
                    };
                    map[key] = item;
                }
                if (!item.Voters.Contains(seat.Label))
                {
                    item.Votes++;
                    item.Voters.Add(seat.Label);
                }
                if (string.IsNullOrEmpty(item.Note) && !string.IsNullOrEmpty(part.Note))
                    item.Note = part.Note;
            }
        }
        int n = Math.Max(1, replies.Count);
        foreach (var item in map.Values)
            item.Grade = Grade(item.Votes, n);
        return map.Values.OrderByDescending(x => x.Votes).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Render(string? need, IReadOnlyList<(JurySeat Seat, string Text)> replies, IReadOnlyList<VoteItem> votes)
    {
        var lines = new List<string>
        {
            Loc.SecSummary,
            Loc.JurySummary(replies.Count, votes.Count(v => v.Grade == Loc.GradeHigh)),
        };
        void Dump(string heading, IEnumerable<VoteItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            lines.Add(heading);
            foreach (var v in list.Take(12))
                lines.Add($"GOTO {v.Path}\t{v.Size}\t{v.Grade} · {v.Votes}/{replies.Count} · {v.Note}");
        }
        Dump(Loc.GradeHigh, votes.Where(v => v.Grade == Loc.GradeHigh));
        Dump(Loc.GradeMid, votes.Where(v => v.Grade == Loc.GradeMid));
        Dump(Loc.GradeLow, votes.Where(v => v.Grade == Loc.GradeLow));
        lines.Add(Loc.SecQuestion);
        lines.Add(Loc.JuryAsk);
        return string.Join(Environment.NewLine, lines);
    }

    static string Grade(int votes, int n)
    {
        if (votes <= 0) return Loc.GradeLow;
        if (votes == n) return Loc.GradeHigh;
        if (votes * 2 >= n) return Loc.GradeMid;
        return Loc.GradeLow;
    }

    static string SizeOf(string label)
    {
        int i = label.LastIndexOf("  ", StringComparison.Ordinal);
        return i < 0 ? "" : label[(i + 2)..].Trim();
    }

    static string Short(string path)
    {
        string p = path.TrimEnd('\\');
        int i = p.LastIndexOf('\\');
        return i < 0 ? p : p[(i + 1)..];
    }

    static bool Protected(string path)
    {
        string p = path.ToLowerInvariant();
        if (p is "c:" or @"c:\" or @"c:\windows" or @"c:\users"
            or @"c:\program files" or @"c:\program files (x86)" or @"c:\programdata")
            return true;
        return p.Contains(@"\windows\winsxs");
    }

    static string Norm(string p) => p.Replace('/', '\\').Trim().TrimEnd('\\');
}
