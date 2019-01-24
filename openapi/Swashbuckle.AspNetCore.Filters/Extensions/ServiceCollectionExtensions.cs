using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Swashbuckle.AspNetCore.Filters
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSwaggerExamples(this IServiceCollection services, JsonSerializerSettings serializerSettings = null)
        {
            services.AddSingleton<SerializerSettingsDuplicator>();
            services.AddSingleton<JsonFormatter>();
            services.AddSingleton<RequestExample>();
            services.AddSingleton<ResponseExample>();
            services.AddSingleton<ExamplesOperationFilter>();
            services.AddSingleton<ServiceProviderExamplesOperationFilter>();

            if (serializerSettings != null)
                services.AddSingleton<JsonSerializerSettings>(serializerSettings);

            return services;
        }

        public static IServiceCollection AddSwaggerExamplesFromAssemblyOf<T>(this IServiceCollection services, JsonSerializerSettings serializerSettings = null)
        {
            AddSwaggerExamples(services, serializerSettings);

            services.Scan(scan => scan
                .FromAssemblyOf<T>()
                    .AddClasses(classes => classes.AssignableTo(typeof(IExamplesProvider<>)))
                    .AsImplementedInterfaces()
                    .WithSingletonLifetime()

                    .AddClasses(classes => classes.AssignableTo(typeof(IExamplesProvider)))
                    .AsSelf()
                    .WithSingletonLifetime()
            );

            return services;
        }
    }
}
