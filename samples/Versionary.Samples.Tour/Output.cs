namespace Versionary.Samples.Tour;

/// <summary>Console formatting, kept in one place so the tours stay about Versionary.</summary>
internal static class Output
{
    public static void Section(int number, string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('━', 78));
        Console.WriteLine($"  {number}. {title}");
        Console.WriteLine(new string('━', 78));
    }

    public static void Note(string text) => Console.WriteLine($"  {text}");

    public static void Result(string label, object? value) => Console.WriteLine($"    {label,-22} {value}");

    public static void Block(string text)
    {
        foreach (var line in text.TrimEnd().Split(Environment.NewLine))
        {
            Console.WriteLine($"    {line}");
        }
    }

    public static void Blank() => Console.WriteLine();
}
