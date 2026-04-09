
using Engie.Mca.Common.Hosting;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddEngieServiceDefaults("mv", "block3-message-validator-.log");

var app = builder.Build();
app.UseEngieServiceDefaults();
app.Run();

public partial class Program;
