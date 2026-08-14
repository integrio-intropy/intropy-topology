namespace Intropy.Topology.Generation.Test;

// Every test that captures Console.Out/Console.Error belongs to this
// collection so xUnit runs them serially. Capture redirects the shared
// console streams; two concurrent captures corrupt one another's output.
[CollectionDefinition("ConsoleCapture")]
public class ConsoleCaptureCollection;
