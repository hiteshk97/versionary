using Versionary.Samples.Tour;
using Versionary.Samples.Tour.Tours;

Console.WriteLine();
Console.WriteLine("  Versionary — a tour of the core, with no mediator involved.");
Console.WriteLine("  Every section below is runnable code in samples/Versionary.Samples.Tour.");

await MultiStepChainTour.RunAsync();
RegistrationStylesTour.Run();
GraphAndValidationTour.RunExplain();
GraphAndValidationTour.RunValidation();
await ResponseFanOutTour.RunAsync();
await OptionsTour.RunAsync();
await UpcastingTour.RunAsync();

Output.Section(8, "Where to go next");
Output.Note("samples/Versionary.Samples.MediatR shows the same chain behind a real HTTP API,");
Output.Note("wired through the MediatR connector, with a pinned version and both strategies.");
Console.WriteLine();
