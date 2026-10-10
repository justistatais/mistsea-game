using Mist.Tests;
using Mist.Tests.Contracts;
using Mist.Tests.Storage;
using Mist.Tests.Content;

if (args.Contains("--storage-child")) return StorageCases.Child(args);
ContractCases.Register();
StorageCases.Register();
ContentCases.Register();
return TestRegistry.Run(args);
