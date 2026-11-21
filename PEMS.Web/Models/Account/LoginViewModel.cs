using System.ComponentModel.DataAnnotations;

namespace PEMS.Web.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Login_Err_UserNameRequired")]
    [Display(Name = "Login_UserName")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Login_Err_PasswordRequired")]
    [DataType(DataType.Password)]
    [Display(Name = "Login_Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Login_RememberMe")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
