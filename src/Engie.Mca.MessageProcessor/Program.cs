
using Engie.Mca.Common.Hosting;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddEngieServiceDefaults("mp", "block2-4-message-processor-.log");

var app = builder.Build();
app.UseEngieServiceDefaults();
app.Run();

public partial class Program;
