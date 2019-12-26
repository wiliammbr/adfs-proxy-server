# ADFS.Proxy.Server

This project presents a simple Proxy Server that helps you on bypassing CORS problems when working with Angular/React projects integrated with ADFS. It may happen when you need to load the JWKs keys, for example.

To get things working, basically you must set the tenant name inside the Web.Config. 

        <add key="ida:RedirectLocation" value="https://adfs.tenant.com.br"/>
        <add key="ida:AuthorizationHost" value="adfs.tenant.com.br:443"/>

So when you request the Keys like https://adfs.tenant.com/adfs/discovery/keys you must request the https://thisproxy.com/proxy/discovery/keys. The JSON will be delivered and it may even be cached by the application.     

This project will be used in my Blog posts [Access ADFS-secured Web API via Angular SPA](https://wiliammbr.com/access-adfs-secured-web-api-via-angular-spa/) and [Setup ADFS to secure Web API and access it through Angular SPA](https://wiliammbr.com/setup-adfs-to-secure-web-api-and-access-it-through-angular-spa/). 
