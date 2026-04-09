
using Engie.Mca.Common.Hosting;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddEngieServiceDefaults("nh", "block5-nack-handler-.log");

var app = builder.Build();
app.UseEngieServiceDefaults();
app.Run();

public partial class Program;
