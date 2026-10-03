using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "FabioSaaS.Auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.LoginPath = "/admin.html";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
var app = builder.Build();
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization();

var dataDir = Path.Combine(app.Environment.ContentRootPath, "Dados");
var uploadDir = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "uploads");
Directory.CreateDirectory(dataDir); Directory.CreateDirectory(uploadDir);
var dbPath = Path.Combine(dataDir, "plataforma.json");
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var gate = new SemaphoreSlim(1, 1);
var hasher = app.Services.GetRequiredService<PasswordHasher<AppUser>>();

async Task<StoreData> ReadData()
{
    if (!File.Exists(dbPath)) return new StoreData();
    try { return JsonSerializer.Deserialize<StoreData>(await File.ReadAllTextAsync(dbPath), json) ?? new StoreData(); }
    catch { throw new InvalidOperationException("O arquivo Dados/plataforma.json não pôde ser lido. Faça uma cópia de segurança antes de qualquer reparo."); }
}
async Task SaveData(StoreData data)
{
    var temp = dbPath + ".tmp";
    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data, json));
    File.Move(temp, dbPath, true);
}
async Task<IResult> Change(Func<StoreData, IResult> action)
{
    await gate.WaitAsync();
    try { var d = await ReadData(); var r = action(d); if (r is IStatusCodeHttpResult s && s.StatusCode is >= 400) return r; await SaveData(d); return r; }
    finally { gate.Release(); }
}
string Slugify(string s) { var t = new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()); while (t.Contains("--")) t = t.Replace("--", "-"); return t.Trim('-'); }
IResult Fail(string message, int status = 400) => Results.Json(new { message }, statusCode: status);
AppUser? CurrentUser(ClaimsPrincipal p) => int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? new AppUser { Id = id, TenantId = int.TryParse(p.FindFirstValue("tenant_id"), out var tid) ? tid : 0, Name = p.Identity?.Name ?? "", Email = p.FindFirstValue(ClaimTypes.Email) ?? "", Role = p.FindFirstValue(ClaimTypes.Role) ?? "" } : null;
bool IsTenantAdmin(ClaimsPrincipal p, int tenantId) => p.Identity?.IsAuthenticated == true && p.IsInRole("TenantAdmin") && int.TryParse(p.FindFirstValue("tenant_id"), out var id) && id == tenantId;
bool IsPlatformAdmin(ClaimsPrincipal p) => p.Identity?.IsAuthenticated == true && p.IsInRole("PlatformAdmin");

// Inicialização: dados demonstrativos separados do banco antigo; nenhum banco anterior é apagado.
await gate.WaitAsync();
try
{
    var d = await ReadData();
    if (d.Tenants.Count == 0)
    {
        var fabio = new Tenant { Id = 1, Name = "Fábio Representações", Slug = "fabio", Tagline = "Materiais para construção", Phone = "", Active = true };
        d.Tenants.Add(fabio);
        d.Products.AddRange(new[] {
            new Product { Id=1,TenantId=1,Name="Assento Sanitário",Code="ASS-001",Category="Banheiro",Description="Assento sanitário branco",Price=79.90m,Stock=25,MinimumStock=5 },
            new Product { Id=2,TenantId=1,Name="Bacia Sanitária Modelo X",Code="BAC-001",Category="Banheiro",Description="Modelo de demonstração",Price=299.90m,Stock=12,MinimumStock=3 },
            new Product { Id=3,TenantId=1,Name="Boia para Caixa d’Água",Code="BOI-001",Category="Hidráulica",Description="Boia de reposição",Price=49.90m,Stock=30,MinimumStock=8 },
            new Product { Id=4,TenantId=1,Name="Caixa de Descarga",Code="CAD-001",Category="Hidráulica",Description="Caixa de descarga",Price=129.90m,Stock=15,MinimumStock=4 }
        });
    }
    if (d.Users.All(x => x.Role != "PlatformAdmin"))
    {
        var u = new AppUser { Id = d.NextUserId++, TenantId = 0, Name = "Administrador da Plataforma", Email = "admin@fabio.local", Role = "PlatformAdmin" };
        u.PasswordHash = hasher.HashPassword(u, "Trocar@12345"); d.Users.Add(u);
    }
    if (d.Users.All(x => x.Email != "fabio@demo.local"))
    {
        var u = new AppUser { Id = d.NextUserId++, TenantId = 1, Name = "Fábio", Email = "fabio@demo.local", Role = "TenantAdmin" };
        u.PasswordHash = hasher.HashPassword(u, "Demo@12345"); d.Users.Add(u);
    }
    await SaveData(d);
}
finally { gate.Release(); }

app.MapGet("/api/store/{slug}", async (string slug) => {
    var d = await ReadData(); var t = d.Tenants.FirstOrDefault(x => x.Slug == slug && x.Active);
    return t is null ? (IResult)Results.NotFound(new { message = "Loja não encontrada." }) : Results.Ok(new { t.Id, t.Name, t.Slug, t.Tagline, t.Phone, t.LogoUrl, t.PrimaryColor });
});
app.MapGet("/api/store/{slug}/products", async (string slug) => {
    var d = await ReadData(); var t = d.Tenants.FirstOrDefault(x => x.Slug == slug && x.Active); if (t is null) return Results.NotFound();
    return Results.Ok(d.Products.Where(p => p.TenantId == t.Id && p.Active).OrderBy(p => p.Category).ThenBy(p => p.Name).Select(p => new { p.Id,p.Name,p.Code,p.Category,p.Description,p.Price,p.Stock,p.ImageUrl,Available=p.Stock>0 }));
});
app.MapPost("/api/auth/login", async (HttpContext ctx, LoginInput input) => {
    var d = await ReadData(); var email = input.Email.Trim().ToLowerInvariant();
    var u = d.Users.FirstOrDefault(x => x.Email == email && (input.TenantSlug == null || x.TenantId == d.Tenants.FirstOrDefault(t => t.Slug == input.TenantSlug)?.Id));
    if (u is null || hasher.VerifyHashedPassword(u, u.PasswordHash, input.Password) == PasswordVerificationResult.Failed) return Fail("E-mail ou senha inválidos.", 401);
    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier,u.Id.ToString()), new(ClaimTypes.Name,u.Name), new(ClaimTypes.Email,u.Email), new(ClaimTypes.Role,u.Role), new("tenant_id",u.TenantId.ToString()) };
    if (u.TenantId > 0) claims.Add(new Claim("tenant_slug", d.Tenants.First(t=>t.Id==u.TenantId).Slug));
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { u.Name, u.Email, u.Role, TenantId=u.TenantId });
});
app.MapPost("/api/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); }).RequireAuthorization();
app.MapGet("/api/auth/me", (ClaimsPrincipal p) => p.Identity?.IsAuthenticated == true ? (IResult)Results.Ok(new { Name=p.Identity.Name, Email=p.FindFirstValue(ClaimTypes.Email), Role=p.FindFirstValue(ClaimTypes.Role), TenantId=p.FindFirstValue("tenant_id"), TenantSlug=p.FindFirstValue("tenant_slug") }) : Results.Unauthorized());

app.MapGet("/api/platform/tenants", async (ClaimsPrincipal p) => {
    if (!IsPlatformAdmin(p)) return Results.Forbid(); var d=await ReadData(); return Results.Ok(d.Tenants.OrderBy(t=>t.Name));
});
app.MapPost("/api/platform/tenants", async (ClaimsPrincipal p, NewTenantInput input) => {
    if (!IsPlatformAdmin(p)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.AdminName) || string.IsNullOrWhiteSpace(input.AdminEmail) || input.AdminPassword.Length < 10) return Fail("Informe nome da loja, nome do representante, e-mail e senha com pelo menos 10 caracteres.");
    return await Change(d => {
        var slug = string.IsNullOrWhiteSpace(input.Slug) ? Slugify(input.Name) : Slugify(input.Slug);
        if (slug.Length < 2 || d.Tenants.Any(t=>t.Slug==slug)) return Fail("Esse endereço de loja já existe. Escolha outro.",409);
        var email=input.AdminEmail.Trim().ToLowerInvariant(); if(d.Users.Any(u=>u.Email==email)) return Fail("Esse e-mail já está cadastrado.",409);
        var t=new Tenant{Id=d.NextTenantId++,Name=input.Name.Trim(),Slug=slug,Tagline=input.Tagline?.Trim()??"",Phone=input.Phone?.Trim()??"",Active=true,PrimaryColor="#17385f"}; d.Tenants.Add(t);
        var u=new AppUser{Id=d.NextUserId++,TenantId=t.Id,Name=input.AdminName.Trim(),Email=email,Role="TenantAdmin"}; u.PasswordHash=hasher.HashPassword(u,input.AdminPassword); d.Users.Add(u);
        return Results.Ok(new { tenant=t, adminEmail=u.Email, storeUrl=$"/index.html?loja={slug}", adminUrl=$"/admin.html?loja={slug}" });
    });
});

app.MapGet("/api/admin/me", async (ClaimsPrincipal p) => {
    if (p.Identity?.IsAuthenticated != true || !p.IsInRole("TenantAdmin")) return Results.Unauthorized();
    var id=int.Parse(p.FindFirstValue("tenant_id")!); var d=await ReadData(); var t=d.Tenants.FirstOrDefault(x=>x.Id==id); return t is null?(IResult)Results.NotFound():Results.Ok(t);
});
app.MapPut("/api/admin/settings", async (ClaimsPrincipal p, TenantSettingsInput input) => {
    if (!int.TryParse(p.FindFirstValue("tenant_id"),out var id) || !IsTenantAdmin(p,id)) return Results.Forbid();
    return await Change(d=>{var t=d.Tenants.FirstOrDefault(x=>x.Id==id);if(t is null)return Results.NotFound();if(string.IsNullOrWhiteSpace(input.Name))return Fail("Informe o nome comercial.");t.Name=input.Name.Trim();t.Tagline=input.Tagline?.Trim()??"";t.Phone=input.Phone?.Trim()??"";t.PrimaryColor=string.IsNullOrWhiteSpace(input.PrimaryColor)?"#17385f":input.PrimaryColor;return Results.Ok(t);});
});
app.MapPost("/api/admin/logo", async (HttpContext ctx) => {
    if (!int.TryParse(ctx.User.FindFirstValue("tenant_id"),out var id) || !IsTenantAdmin(ctx.User,id)) return Results.Forbid();
    if (!ctx.Request.HasFormContentType) return Fail("Envie um arquivo de imagem."); var form=await ctx.Request.ReadFormAsync();var file=form.Files.GetFile("file");
    if(file is null||file.Length==0||file.Length>2_000_000)return Fail("Selecione uma imagem de até 2 MB.");
    var ext=Path.GetExtension(file.FileName).ToLowerInvariant();if(ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")return Fail("Use PNG, JPG ou WEBP.");
    var name=$"logo-{id}-{Guid.NewGuid():N}{ext}";await using(var fs=File.Create(Path.Combine(uploadDir,name)))await file.CopyToAsync(fs);
    return await Change(d=>{var t=d.Tenants.FirstOrDefault(x=>x.Id==id);if(t is null)return Results.NotFound();t.LogoUrl="/uploads/"+name;return Results.Ok(new{t.LogoUrl});});
});
app.MapGet("/api/admin/products", async (ClaimsPrincipal p) => {
    if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var d=await ReadData();return Results.Ok(d.Products.Where(x=>x.TenantId==id).OrderBy(x=>x.Name));
});
app.MapPost("/api/admin/products", async (ClaimsPrincipal p, ProductInput input) => {
    if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();
    if(string.IsNullOrWhiteSpace(input.Name)||input.Price<0||input.Stock<0||input.MinimumStock<0)return Fail("Confira nome, preço e quantidades.");
    return await Change(d=>{Product? pr=input.Id>0?d.Products.FirstOrDefault(x=>x.Id==input.Id&&x.TenantId==id):null;if(input.Id>0&&pr is null)return Results.NotFound();if(pr is null){pr=new Product{Id=d.NextProductId++,TenantId=id};d.Products.Add(pr);}pr.Name=input.Name.Trim();pr.Code=input.Code?.Trim()??"";pr.Category=input.Category?.Trim()??"";pr.Description=input.Description?.Trim()??"";pr.Price=input.Price;pr.Stock=input.Stock;pr.MinimumStock=input.MinimumStock;pr.Active=true;return Results.Ok(pr);});
});
app.MapPost("/api/admin/products/{productId:int}/stock", async (ClaimsPrincipal p,int productId,StockInput input)=>{
    if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();if(input.Quantity<=0)return Fail("Informe uma quantidade maior que zero.");
    return await Change(d=>{var pr=d.Products.FirstOrDefault(x=>x.Id==productId&&x.TenantId==id);if(pr is null)return Results.NotFound();pr.Stock+=input.Quantity;return Results.Ok(new{pr.Id,pr.Name,pr.Stock,LowStock=pr.Stock<=pr.MinimumStock});});
});
app.MapPost("/api/admin/products/{productId:int}/image", async (HttpContext ctx,int productId)=>{
    if(!int.TryParse(ctx.User.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(ctx.User,id))return Results.Forbid();var form=await ctx.Request.ReadFormAsync();var file=form.Files.GetFile("file");if(file is null||file.Length==0||file.Length>5_000_000)return Fail("Selecione uma foto de até 5 MB.");var ext=Path.GetExtension(file.FileName).ToLowerInvariant();if(ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")return Fail("Use JPG, PNG ou WEBP.");var name=$"product-{id}-{Guid.NewGuid():N}{ext}";await using(var fs=File.Create(Path.Combine(uploadDir,name)))await file.CopyToAsync(fs);return await Change(d=>{var pr=d.Products.FirstOrDefault(x=>x.Id==productId&&x.TenantId==id);if(pr is null)return Results.NotFound();pr.ImageUrl="/uploads/"+name;return Results.Ok(pr);});
});
app.MapPost("/api/admin/products/{productId:int}/archive", async(ClaimsPrincipal p,int productId)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();return await Change(d=>{var x=d.Products.FirstOrDefault(v=>v.Id==productId&&v.TenantId==id);if(x is null)return Results.NotFound();x.Active=false;return Results.Ok();});});
app.MapGet("/api/admin/orders", async(ClaimsPrincipal p)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var d=await ReadData();return Results.Ok(d.Orders.Where(o=>o.TenantId==id).OrderByDescending(o=>o.CreatedAt));});
app.MapGet("/api/admin/trash", async(ClaimsPrincipal p)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var d=await ReadData();return Results.Ok(d.Orders.Where(o=>o.TenantId==id&&o.InTrash).OrderByDescending(o=>o.CreatedAt));});
app.MapPost("/api/admin/orders/{orderId:int}/trash",async(ClaimsPrincipal p,int orderId)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();return await Change(d=>{var o=d.Orders.FirstOrDefault(x=>x.Id==orderId&&x.TenantId==id);if(o is null)return Results.NotFound();o.InTrash=true;return Results.Ok();});});
app.MapPost("/api/admin/orders/{orderId:int}/restore",async(ClaimsPrincipal p,int orderId)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();return await Change(d=>{var o=d.Orders.FirstOrDefault(x=>x.Id==orderId&&x.TenantId==id);if(o is null)return Results.NotFound();o.InTrash=false;return Results.Ok();});});
app.MapDelete("/api/admin/orders/{orderId:int}",async(ClaimsPrincipal p,int orderId)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();return await Change(d=>{var n=d.Orders.RemoveAll(x=>x.Id==orderId&&x.TenantId==id);return n==0?(IResult)Results.NotFound():Results.Ok();});});
app.MapGet("/api/admin/customers", async(ClaimsPrincipal p)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var d=await ReadData();return Results.Ok(d.Customers.Where(c=>c.TenantId==id).OrderBy(c=>c.Name));});
app.MapGet("/api/admin/summary",async(ClaimsPrincipal p)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var d=await ReadData();var orders=d.Orders.Where(o=>o.TenantId==id&&!o.InTrash).ToList();var products=d.Products.Where(x=>x.TenantId==id&&x.Active).ToList();return Results.Ok(new{orders=orders.Count,pending=orders.Count(o=>o.Status=="Recebido"),revenue=orders.Where(o=>o.Status!="Cancelado").Sum(o=>o.Total),products=products.Count,lowStock=products.Count(x=>x.Stock<=x.MinimumStock)});});
app.MapPatch("/api/admin/orders/{orderId:int}/status",async(ClaimsPrincipal p,int orderId,StatusInput input)=>{if(!int.TryParse(p.FindFirstValue("tenant_id"),out var id)||!IsTenantAdmin(p,id))return Results.Forbid();var allowed=new[]{"Recebido","Em separação","Enviado","Concluído","Cancelado"};if(!allowed.Contains(input.Status))return Fail("Status inválido.");return await Change(d=>{var o=d.Orders.FirstOrDefault(x=>x.Id==orderId&&x.TenantId==id);if(o is null)return Results.NotFound();if(o.Status!="Cancelado"&&input.Status=="Cancelado"){foreach(var line in o.Items){var pr=d.Products.FirstOrDefault(x=>x.Id==line.ProductId&&x.TenantId==id);if(pr is not null)pr.Stock+=line.Quantity;}}else if(o.Status=="Cancelado"&&input.Status!="Cancelado"){foreach(var line in o.Items){var pr=d.Products.FirstOrDefault(x=>x.Id==line.ProductId&&x.TenantId==id);if(pr is null||pr.Stock<line.Quantity)return Fail("Não é possível reabrir o pedido: estoque insuficiente para "+line.ProductName+".");}foreach(var line in o.Items)d.Products.First(x=>x.Id==line.ProductId&&x.TenantId==id).Stock-=line.Quantity;}o.Status=input.Status;return Results.Ok(o);});});
app.MapPost("/api/store/{slug}/orders",async(string slug,NewOrder input)=>{
    if(input.Items.Count==0||string.IsNullOrWhiteSpace(input.CustomerName)||string.IsNullOrWhiteSpace(input.Phone))return Fail("Informe nome, telefone e produtos.");
    return await Change(d=>{var t=d.Tenants.FirstOrDefault(x=>x.Slug==slug&&x.Active);if(t is null)return Results.NotFound();var lines=new List<OrderLine>();foreach(var requested in input.Items){if(requested.Quantity<1)return Fail("Quantidade inválida.");var pr=d.Products.FirstOrDefault(x=>x.Id==requested.ProductId&&x.TenantId==t.Id&&x.Active);if(pr is null)return Fail("Produto não encontrado.");if(pr.Stock<requested.Quantity)return Fail($"Estoque insuficiente para {pr.Name}. Disponível: {pr.Stock}.");lines.Add(new OrderLine{ProductId=pr.Id,Code=pr.Code,ProductName=pr.Name,Quantity=requested.Quantity,UnitPrice=pr.Price,Total=pr.Price*requested.Quantity});}
      foreach(var line in lines)d.Products.First(x=>x.Id==line.ProductId&&x.TenantId==t.Id).Stock-=line.Quantity;
      var order=new Order{Id=d.NextOrderId++,TenantId=t.Id,CustomerName=input.CustomerName.Trim(),Phone=input.Phone.Trim(),Email=input.Email?.Trim()??"",Address=input.Address?.Trim()??"",Notes=input.Notes?.Trim()??"",CreatedAt=DateTimeOffset.Now,Status="Recebido",Items=lines,Total=lines.Sum(x=>x.Total)};d.Orders.Add(order);
      var customer=d.Customers.FirstOrDefault(x=>x.TenantId==t.Id&&x.Phone==order.Phone);if(customer is null){customer=new Customer{Id=d.NextCustomerId++,TenantId=t.Id,Name=order.CustomerName,Phone=order.Phone};d.Customers.Add(customer);}customer.Name=order.CustomerName;customer.Email=order.Email;customer.Address=order.Address;customer.LastOrder=DateTimeOffset.Now;
      return Results.Ok(new{order.Id,order.Total,order.Status,order.CreatedAt,Message="Pedido recebido. O representante confirmará a entrega."});});
});

app.Run();

public class StoreData { public List<Tenant> Tenants{get;set;}=[];public List<AppUser> Users{get;set;}=[];public List<Product> Products{get;set;}=[];public List<Order> Orders{get;set;}=[];public List<Customer> Customers{get;set;}=[];public int NextTenantId{get;set;}=2;public int NextUserId{get;set;}=1;public int NextProductId{get;set;}=5;public int NextOrderId{get;set;}=1001;public int NextCustomerId{get;set;}=1;}
public class Tenant { public int Id{get;set;} public string Name{get;set;}="";public string Slug{get;set;}="";public string Tagline{get;set;}="";public string Phone{get;set;}="";public string? LogoUrl{get;set;}public string PrimaryColor{get;set;}="#17385f";public bool Active{get;set;}=true; }
public class AppUser { public int Id{get;set;}public int TenantId{get;set;}public string Name{get;set;}="";public string Email{get;set;}="";public string PasswordHash{get;set;}="";public string Role{get;set;}="TenantAdmin"; }
public class Product {public int Id{get;set;}public int TenantId{get;set;}public string Name{get;set;}="";public string Code{get;set;}="";public string Category{get;set;}="";public string Description{get;set;}="";public decimal Price{get;set;}public int Stock{get;set;}public int MinimumStock{get;set;}public string? ImageUrl{get;set;}public bool Active{get;set;}=true;}
public class Order {public int Id{get;set;}public int TenantId{get;set;}public string CustomerName{get;set;}="";public string Phone{get;set;}="";public string Email{get;set;}="";public string Address{get;set;}="";public string Notes{get;set;}="";public DateTimeOffset CreatedAt{get;set;}public string Status{get;set;}="Recebido";public List<OrderLine> Items{get;set;}=[];public decimal Total{get;set;}public bool InTrash{get;set;}}
public class OrderLine {public int ProductId{get;set;}public string Code{get;set;}="";public string ProductName{get;set;}="";public int Quantity{get;set;}public decimal UnitPrice{get;set;}public decimal Total{get;set;}}
public class Customer {public int Id{get;set;}public int TenantId{get;set;}public string Name{get;set;}="";public string Phone{get;set;}="";public string Email{get;set;}="";public string Address{get;set;}="";public DateTimeOffset LastOrder{get;set;}}
public record LoginInput(string Email,string Password,string? TenantSlug);
public record NewTenantInput(string Name,string? Slug,string? Tagline,string? Phone,string AdminName,string AdminEmail,string AdminPassword);
public record TenantSettingsInput(string Name,string? Tagline,string? Phone,string? PrimaryColor);
public record ProductInput(int Id,string Name,string? Code,string? Category,string? Description,decimal Price,int Stock,int MinimumStock);
public record StockInput(int Quantity);
public record NewOrder(string CustomerName,string Phone,string? Email,string? Address,string? Notes,List<CartItem> Items);
public record CartItem(int ProductId,int Quantity);
public record StatusInput(string Status);
