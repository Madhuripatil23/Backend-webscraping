using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using webscrapperapi.Services;
using webscrapperapi.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ✅ Register services and CORS before Build()
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IScraperService, ScraperService>();
builder.Services.AddScoped<IScraperRepository, ScraperRepository>();
builder.Services.AddScoped<GeminiService>();

// ✅ Add CORS here
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactDevClient", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ✅ Apply the CORS policy
app.UseCors("AllowReactDevClient");

app.UseAuthorization();
app.MapControllers();

app.Run();
