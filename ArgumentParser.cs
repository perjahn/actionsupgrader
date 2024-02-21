using System.Collections.Generic;

class ArgumentParser
{
    public static string String(List<string> args, string flagname, string defaultValue)
    {
        var index = args.IndexOf(flagname);
        if (index < 0 || index > args.Count - 2)
        {
            return defaultValue;
        }

        var value = args[index + 1];
        args.RemoveRange(index, 2);
        return value;
    }

    public static bool Flag(List<string> args, string flagname)
    {
        var index = args.IndexOf(flagname);
        if (index < 0)
        {
            return false;
        }

        args.RemoveAt(index);
        return true;
    }

    public static int Int(List<string> args, string flagname, int defaultValue)
    {
        var index = args.IndexOf(flagname);
        if (index < 0 || index > args.Count - 2)
        {
            return defaultValue;
        }

        var value = args[index + 1];
        args.RemoveRange(index, 2);
        return int.TryParse(value, out int intValue) ? intValue : defaultValue;
    }
}
