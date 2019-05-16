namespace WebUntisProxy

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

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
        match response.Headers.TryGetValues("Set-Cookie") with
        | (true, cookies) -> String.concat ";;;" cookies |> printfn "============ %s"
        | _ -> ()
        ()
    }

type Startup() =

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    member this.ConfigureServices(services: IServiceCollection) =
        let host = Environment.getEnvironmentVariableOrFail "webUntisHost" |> Uri
        services
            .AddHttpClient(HttpClientNames.webUntis, fun c ->
                c.BaseAddress <- host
            )
            .ConfigurePrimaryHttpMessageHandler(fun () ->
                let cookieContainer = CookieContainer()
                let schoolName =
                    Environment.getEnvironmentVariableOrFail "schoolName"
                    |> Encoding.UTF8.GetBytes
                    |> Convert.ToBase64String
                    |> sprintf "\"_%s\"" // No idea why WebUntis adds surrounding quotes '"' and a leading underscore '_'
                cookieContainer.Add(Cookie("schoolname", schoolName, "/WebUntis", host.Host))
                new HttpClientHandler(CookieContainer = cookieContainer) :> HttpMessageHandler
            )
        |> ignore

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member this.Configure(app: IApplicationBuilder, env: IHostingEnvironment) =
        if env.IsDevelopment() then 
            app.UseDeveloperExceptionPage() |> ignore

        app.Run (RequestDelegate(Main.proxy >> Async.StartAsTask >> fun t -> t :> Task))