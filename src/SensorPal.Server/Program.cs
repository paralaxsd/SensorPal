using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

namespace SensorPal.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        var startedAt = DateTimeOffset.Now;
        app.MapGet("/status", () => new StatusDto
        {
            Name = "SensorPal",
            StartedAt = startedAt,
            Now = DateTimeOffset.Now,
            Mode = "Idle"
        });

        app.Run();
    }
}