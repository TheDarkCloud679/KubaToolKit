using System;
using System.Collections.Generic;
using System.Linq;

namespace KubaToolKit.Modules.ApiClient;

// Postman's built-in "dynamic variables" (https://learning.postman.com/docs/tests-and-scripts/write-scripts/variables-list/)
// -- each one generates a fresh value on every request instead of reading
// one from the selected environment. Covers the subset actually seen in
// real KubaAPI collections; an unrecognized {{$name}} is left untouched by
// the caller, which then reports it as unresolved just like a missing
// {{var}}.
internal static class PostmanDynamicVariables
{
    private static readonly Random Random = new();

    private static readonly string[] FirstNames =
    {
        "Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Jamie", "Avery",
        "Chris", "Sam", "Drew", "Robin", "Quinn", "Reese", "Dana", "Leslie"
    };

    private static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller",
        "Davis", "Martinez", "Lopez", "Wilson", "Anderson", "Taylor", "Moore"
    };

    private static readonly string[] Cities =
    {
        "Paris", "Lyon", "Marseille", "Toulouse", "Nantes", "Bordeaux",
        "Lille", "Strasbourg", "Rennes", "Nice"
    };

    private static readonly string[] Countries =
    {
        "France", "Belgium", "Switzerland", "Germany", "Spain", "Italy",
        "Canada", "United States", "United Kingdom", "Portugal"
    };

    private static readonly string[] CountryCodes =
    {
        "FR", "BE", "CH", "DE", "ES", "IT", "CA", "US", "GB", "PT"
    };

    private static readonly string[] Words =
    {
        "apple", "bridge", "candle", "diamond", "engine", "forest", "garden",
        "harbor", "island", "jungle", "kitchen", "lantern", "mountain",
        "needle", "ocean", "pencil", "quarter", "river", "shadow", "temple"
    };

    public static bool
    TryGenerate(
        string name,
        out string value)
    {
        switch (name)
        {
            case "$guid":
            case "$randomUUID":

                value = Guid.NewGuid().ToString();

                return true;

            case "$timestamp":

                value = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                return true;

            case "$isoTimestamp":

                value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                return true;

            case "$randomInt":

                value = Random.Next(0, 1001).ToString();

                return true;

            case "$randomBoolean":

                value = Random.Next(0, 2) == 0 ? "false" : "true";

                return true;

            case "$randomFirstName":

                value = Pick(FirstNames);

                return true;

            case "$randomLastName":

                value = Pick(LastNames);

                return true;

            case "$randomFullName":

                value = $"{Pick(FirstNames)} {Pick(LastNames)}";

                return true;

            case "$randomEmail":

                value =
                    $"{Pick(FirstNames)}.{Pick(LastNames)}{Random.Next(1, 999)}@example.com"
                        .ToLowerInvariant();

                return true;

            case "$randomCity":

                value = Pick(Cities);

                return true;

            case "$randomCountry":

                value = Pick(Countries);

                return true;

            case "$randomCountryCode":

                value = Pick(CountryCodes);

                return true;

            case "$randomStreetAddress":

                value = $"{Random.Next(1, 999)} {Pick(Words)} Street";

                return true;

            case "$randomWord":

                value = Pick(Words);

                return true;

            case "$randomWords":

                value = string.Join(" ", RandomWords(3));

                return true;

            case "$randomPhrase":

                value = string.Join(" ", RandomWords(4));

                return true;

            case "$randomLoremSentence":

                value = BuildSentence();

                return true;

            case "$randomLoremSentences":

                value = $"{BuildSentence()} {BuildSentence()} {BuildSentence()}";

                return true;

            default:

                value = "";

                return false;
        }
    }

    private static string
    Pick(
        string[] options) =>
        options[Random.Next(options.Length)];

    private static IEnumerable<string>
    RandomWords(
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return Pick(Words);
        }
    }

    private static string
    BuildSentence()
    {
        var words = RandomWords(Random.Next(5, 10)).ToList();

        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];

        return string.Join(" ", words) + ".";
    }
}
