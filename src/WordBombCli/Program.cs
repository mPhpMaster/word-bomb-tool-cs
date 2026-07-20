// wbt-cli -- GUI-less interface to the Word Bomb Tool: word suggestions and
// definitions via the Datamuse API. Port of cli.py / cmd/wordbombcli/main.go.
using System.Text.Json;
using WordBombTool;

namespace WordBombTool.Cli;

public static class Program
{
    private static readonly Dictionary<string, string> SearchAliases = new()
    {
        ["starts-with"] = "Starts With", ["starts"] = "Starts With", ["sw"] = "Starts With",
        ["ends-with"] = "Ends With", ["ends"] = "Ends With", ["ew"] = "Ends With",
        ["contains"] = "Contains", ["c"] = "Contains",
        ["rhymes"] = "Rhymes", ["r"] = "Rhymes",
        ["related"] = "Related Words", ["related-words"] = "Related Words", ["rel"] = "Related Words",
    };

    private static readonly Dictionary<string, string> SortAliases = new()
    {
        ["shortest"] = "Shortest", ["s"] = "Shortest",
        ["longest"] = "Longest", ["l"] = "Longest",
        ["random"] = "Random", ["rand"] = "Random",
        ["frequency"] = "Frequency", ["freq"] = "Frequency", ["f"] = "Frequency",
    };

    public static int Main(string[] args) => Run(args);

    private static int Run(string[] argv)
    {
        if (argv.Length == 0) { Usage(); return 2; }

        var i = 0;
        while (i < argv.Length && (argv[i] == "-v" || argv[i] == "--verbose")) i++;
        argv = argv[i..];
        if (argv.Length == 0) { Usage(); return 2; }

        var cmd = argv[0];
        var rest = argv[1..];
        return cmd switch
        {
            "suggest" => CmdSuggest(rest),
            "define" => CmdDefine(rest),
            "modes" => CmdModes(),
            "-h" or "--help" or "help" => Help(),
            _ => UnknownCommand(cmd),
        };
    }

    private static int Help() { Usage(); return 0; }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"error: unknown command \"{cmd}\"");
        Usage();
        return 2;
    }

    private static void Usage()
    {
        Console.Error.WriteLine("wbt — Word Bomb Tool CLI (word suggestions and definitions via Datamuse)");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  suggest LETTERS [--mode M] [--sort S] [--limit N] [--json] [--pretty-json]");
        Console.Error.WriteLine("  define WORD [--json] [--pretty-json]");
        Console.Error.WriteLine("  modes");
    }

    private static (string? value, List<string> rest) ExtractOption(List<string> args, string longName, string? shortName)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == longName || (shortName != null && args[i] == shortName))
            {
                if (i + 1 >= args.Count) return (null, args);
                var val = args[i + 1];
                var rest = new List<string>(args);
                rest.RemoveRange(i, 2);
                return (val, rest);
            }
        }
        return (null, args);
    }

    private static bool ExtractFlag(List<string> args, string name)
    {
        var idx = args.IndexOf(name);
        if (idx < 0) return false;
        args.RemoveAt(idx);
        return true;
    }

    private static string? ResolveMode(string alias, Dictionary<string, string> mapping, string[] canonical, string label)
    {
        var key = alias.ToLowerInvariant().Trim().Replace('_', '-');
        if (mapping.TryGetValue(key, out var v)) return v;
        foreach (var m in canonical)
        {
            var low = m.ToLowerInvariant();
            if (low == key || low.Replace(' ', '-') == key) return m;
        }
        var keys = mapping.Keys.OrderBy(k => k, StringComparer.Ordinal);
        Console.Error.WriteLine($"error: unknown {label} \"{alias}\"; try one of: {string.Join(", ", keys)}");
        return null;
    }

    private static int CmdSuggest(string[] argvArr)
    {
        var args = argvArr.ToList();
        var (modeOpt, r1) = ExtractOption(args, "--mode", "-m"); args = r1;
        var (sortOpt, r2) = ExtractOption(args, "--sort", "-s"); args = r2;
        var (limitOpt, r3) = ExtractOption(args, "--limit", "-n"); args = r3;
        var asJson = ExtractFlag(args, "--json");
        var prettyJson = ExtractFlag(args, "--pretty-json");

        if (args.Count < 1)
        {
            Console.Error.WriteLine("error: missing LETTERS argument");
            return 2;
        }

        var searchMode = ResolveMode(modeOpt ?? "starts-with", SearchAliases, AppConfig.SearchModes, "search mode");
        if (searchMode == null) return 2;
        var sMode = ResolveMode(sortOpt ?? "shortest", SortAliases, AppConfig.SortModes, "sort mode");
        if (sMode == null) return 2;

        var lim = AppConfig.MaxSuggestionsDisplay;
        if (limitOpt != null && int.TryParse(limitOpt, out var parsedLim)) lim = parsedLim;
        if (lim < 1) lim = 1;
        if (lim > AppConfig.MaxSuggestionsDisplay) lim = AppConfig.MaxSuggestionsDisplay;

        var letters = args[0].Trim();
        if (letters == "")
        {
            Console.Error.WriteLine("error: letters must not be empty");
            return 2;
        }

        var client = new DatamuseClient();
        var raw = client.Suggestions(letters, searchMode);
        var words = SuggestionLogic.Sort(raw, sMode);
        if (words.Count > lim) words = words.GetRange(0, lim);

        if (asJson)
        {
            PrintJson(new
            {
                letters,
                search_mode = searchMode,
                sort_mode = sMode,
                api_status = client.Status(),
                words,
            }, prettyJson);
            return 0;
        }

        Console.WriteLine($"search: {searchMode}  sort: {sMode}  api: {client.Status()}");
        if (words.Count == 0) { Console.WriteLine("(no words)"); return 0; }
        for (var i = 0; i < words.Count; i++) Console.WriteLine($"{i + 1,4}  {words[i]}");
        return 0;
    }

    private static int CmdDefine(string[] argvArr)
    {
        var args = argvArr.ToList();
        var asJson = ExtractFlag(args, "--json");
        var prettyJson = ExtractFlag(args, "--pretty-json");

        if (args.Count < 1)
        {
            Console.Error.WriteLine("error: missing WORD argument");
            return 2;
        }
        var word = args[0].Trim();
        if (word == "")
        {
            Console.Error.WriteLine("error: word must not be empty");
            return 2;
        }

        var client = new DatamuseClient();
        var defs = client.Definitions(word);

        if (asJson)
        {
            PrintJson(new { word, api_status = client.Status(), definitions = defs }, prettyJson);
            return 0;
        }

        Console.WriteLine($"word: {word}  api: {client.Status()}");
        if (defs.Count == 0) { Console.WriteLine("(no definitions)"); return 0; }
        for (var i = 0; i < defs.Count; i++) Console.WriteLine($"{i + 1}. {defs[i]}");
        return 0;
    }

    private static int CmdModes()
    {
        Console.WriteLine("Search modes (use with suggest --mode):");
        foreach (var m in AppConfig.SearchModes) Console.WriteLine($"  - {m}");
        Console.WriteLine();
        Console.WriteLine("Sort modes (use with suggest --sort):");
        foreach (var m in AppConfig.SortModes) Console.WriteLine($"  - {m}");
        Console.WriteLine();
        Console.WriteLine("Aliases examples: starts-with, ends-with, contains, rhymes, related");
        Console.WriteLine("                  shortest, longest, random, frequency");
        return 0;
    }

    private static void PrintJson(object v, bool pretty)
    {
        var opts = new JsonSerializerOptions { WriteIndented = pretty };
        Console.WriteLine(JsonSerializer.Serialize(v, opts));
    }
}
