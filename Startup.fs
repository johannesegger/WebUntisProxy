namespace WebUntisProxy

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open System.Net.Http
open System.Threading.Tasks

module Environment =
    let getEnvironmentVariableOrFail key =
        let value = Environment.GetEnvironmentVariable key
        if isNull value then failwithf "Environment variable \"%s\" is not set" key
        else value

module HttpClientNames =
    let webUntis = "webuntis"

module Main =
    let proxy (context: HttpContext) = async {
        let httpClientFactory = context.RequestServices.GetService<IHttpClientFactory>()
        use httpClient = httpClientFactory.CreateClient(HttpClientNames.webUntis)
        use request = new HttpRequestMessage(HttpMethod context.Request.Method, context.Request.Path.ToString())
        let! response = httpClient.SendAsync(request) |> Async.AwaitTask
        do! response.Content.CopyToAsync(context.Response.Body) |> Async.AwaitTask
        ()
    }

type Startup() =

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    member this.ConfigureServices(services: IServiceCollection) =
        services.AddHttpClient(HttpClientNames.webUntis, fun c ->
            let baseAddress = Environment.getEnvironmentVariableOrFail "webUntisHost" |> Uri
            c.BaseAddress <- baseAddress
        ) |> ignore

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member this.Configure(app: IApplicationBuilder, env: IHostingEnvironment) =
        if env.IsDevelopment() then 
            app.UseDeveloperExceptionPage() |> ignore

        app.Run (RequestDelegate(Main.proxy >> Async.StartAsTask >> fun t -> t :> Task))