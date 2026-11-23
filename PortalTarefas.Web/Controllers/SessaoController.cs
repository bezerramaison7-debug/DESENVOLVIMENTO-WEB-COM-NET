using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

public record Credenciais(string Usuario, string Senha);
public record Sessao(string Usuario, bool Administrador, bool EditarOutraEquipe);

[ApiController, Route("api/sessao")]
public sealed class SessaoController(
    SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [Authorize, HttpGet]
    [ProducesResponseType<Sessao>(200)]
    public Sessao Perfil() => new(User.Identity?.Name ?? "", User.IsInRole("Administrador"),
        User.HasClaim("tarefas", "editar-outra-equipe"));

    [ValidateAntiForgeryToken, HttpPost("login")]
    public async Task<IActionResult> Login(Credenciais credenciais)
    {
        var resultado = await signIn.PasswordSignInAsync(
            credenciais.Usuario, credenciais.Senha, isPersistent: false, lockoutOnFailure: true);
        return resultado.Succeeded ? NoContent() : Problem(statusCode: 401, title: "Credenciais inválidas.");
    }

    [Authorize, ValidateAntiForgeryToken, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var usuario = await users.GetUserAsync(User);
        if (usuario is not null && !(await users.UpdateSecurityStampAsync(usuario)).Succeeded)
            return Problem(statusCode: 500, title: "Não foi possível encerrar a sessão.");
        await signIn.SignOutAsync();
        return NoContent();
    }
}
