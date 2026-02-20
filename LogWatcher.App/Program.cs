using LogWatcher.App;

var rootCommand = CommandConfiguration.CreateRootCommand();
var parseResult = rootCommand.Parse(args);
return parseResult.Invoke();