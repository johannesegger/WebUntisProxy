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
open Microsoft.Net.Http.Headers
open Microsoft.Extensions.Primitives

module Environment =
    let private getEnvironmentVariableOrFail key =
        let value = Environment.GetEnvironmentVariable key
        if isNull value then failwithf "Environment variable \"%s\" is not set" key
        else value

    let schoolName =
        getEnvironmentVariableOrFail "schoolName"
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String
        |> sprintf "\"_%s\"" // No idea why WebUntis adds surrounding quotes '"' and a leading underscore '_'

    let host =
        getEnvironmentVariableOrFail "webUntisHost"
        |> Uri

module HttpClientNames =
    let webUntis = "webuntis"

module Main =
    let proxy (context: HttpContext) = async {
        let httpClientFactory = context.RequestServices.GetService<IHttpClientFactory>()
        use httpClient = httpClientFactory.CreateClient(HttpClientNames.webUntis)
        let uri = sprintf "%s%s" context.Request.Path.Value context.Request.QueryString.Value
        use request = new HttpRequestMessage(HttpMethod context.Request.Method, uri)

        context.Request.Cookies
        |> Seq.map (fun c -> c.Key, c.Value)
        |> Map.ofSeq
        |> Map.add "schoolname" Environment.schoolName
        |> Map.toSeq
        |> Seq.map (fun (key, value) -> sprintf "%s=%s" key value)
        |> String.concat ";"
        |> fun value -> request.Headers.Add("Cookie", value)
        
        let! response = httpClient.SendAsync(request) |> Async.AwaitTask

        // response.Headers
        // |> Seq.iter (fun h ->
        //     context.Response.Headers.Add(h.Key, StringValues(Seq.toArray h.Value))
        // )

        response.Content.Headers
        |> Seq.iter (fun h ->
            context.Response.Headers.Add(h.Key, StringValues(Seq.toArray h.Value))
        )

        match response.Headers.TryGetValues("Set-Cookie") with
        | (true, cookies) ->
            cookies
            |> System.Collections.Generic.List<_>
            |> SetCookieHeaderValue.ParseList
            |> Seq.iter (fun c ->
                let cookieOptions =
                    CookieOptions(
                        Domain = c.Domain.Value,
                        Path = c.Path.Value,
                        Expires = c.Expires,
                        Secure = c.Secure,
                        SameSite =
                            (match c.SameSite with
                            | SameSiteMode.None -> Microsoft.AspNetCore.Http.SameSiteMode.None
                            | SameSiteMode.Strict -> Microsoft.AspNetCore.Http.SameSiteMode.Strict
                            | SameSiteMode.Lax | _ -> Microsoft.AspNetCore.Http.SameSiteMode.Lax),
                        HttpOnly = c.HttpOnly,
                        MaxAge = c.MaxAge)
                        // IsEssential = c.)
                context.Response.Cookies.Append(c.Name.Value, c.Value.Value, cookieOptions))
        | _ -> ()

        do! response.Content.CopyToAsync(context.Response.Body) |> Async.AwaitTask

        ()
    }

type Startup() =

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    member this.ConfigureServices(services: IServiceCollection) =
        services
            .AddHttpClient(HttpClientNames.webUntis, fun c ->
                c.BaseAddress <- Environment.host
            )
            .ConfigurePrimaryHttpMessageHandler(fun () ->
                let cookieContainer = CookieContainer()
                
                cookieContainer.Add(Cookie("schoolname", Environment.schoolName, "/WebUntis", Environment.host.Host))
                new HttpClientHandler(CookieContainer = cookieContainer) :> HttpMessageHandler
            )
        |> ignore

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    member this.Configure(app: IApplicationBuilder, env: IHostingEnvironment) =
        if env.IsDevelopment() then 
            app.UseDeveloperExceptionPage() |> ignore

        app.Run (RequestDelegate(Main.proxy >> Async.StartAsTask >> fun t -> t :> Task))