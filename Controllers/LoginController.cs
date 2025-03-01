﻿using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using accountservice.ServiceFactory;
using accountservice.Interfaces;
using accountservice.ForcedModels;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.DataProtection;
using accountservice.Commons;

namespace accountservice.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LoginController : ControllerBase
    {

        private readonly IConfiguration _config;
        private readonly IDataProtectionProvider _idp;
        private ILogin? loginService;
        public LoginController(IConfiguration config, IDataProtectionProvider idp)
        {
            _config = config;
            _idp = idp;

        }


        [HttpGet]
        [Route("Loginwithmicrosoft")]
        public async Task<IActionResult> LoginwithMicrosoft(string? code)
        {
            loginService = ServicesFactory.GetLoginService(HttpContext, _config, loginService, _idp);

            //Get login url
            //Should be tested against whitelist url. Though microsoft does that
            string loginurl = HttpContext.Request.GetEncodedUrl();

            int queryIndex = loginurl.IndexOf('?') < 0 ? loginurl.Length : loginurl.IndexOf('?');

            loginurl = loginurl.Substring(0, queryIndex);

            return await loginService.LoginwithMicrosoft(code, loginurl);

        }

        [HttpPost("Loginwithmicrosoft")]
        public async Task<IActionResult> LoginwithMicrosoft([FromBody] MUser user, int? phonecode)
        {
            if (ModelState.IsValid)
            {
                var authorization = HttpContext.Request.Headers.Authorization;
                if (authorization.Count > 0)
                {
                    string token = authorization[0].Substring("Bearer ".Length).Trim();

                    loginService = ServicesFactory.GetLoginService(HttpContext, _config, loginService, _idp);


                    return await loginService.HandleOAuthUserRegistration(user, token, phonecode);

                }




                //User not authorized
                return Unauthorized();
            }
            else
            {
                return BadRequest();
            }
        }

        [HttpGet("verify_phone")]
        public async Task<IActionResult> VerifyUserPhone([FromQuery]string userphone, [FromQuery]string? code)
        {
           
            var authorization = HttpContext.Request.Headers.Authorization;
            if (authorization.Count > 0)
            {
                string token = authorization[0].Substring("Bearer ".Length).Trim();

                loginService = ServicesFactory.GetLoginService(HttpContext, _config, loginService, _idp);

                //Verify phone if code exists or generate code
               
                code = code ?? string.Empty;
                if (string.IsNullOrEmpty(code))
                {
                    
                    return await loginService.GeneratePhoneCode(userphone, token);
                }
                else //Verify phone code
                {
                    int intCode;
                    int.TryParse(code ?? "0", out intCode);

                    
                    return intCode > 0 ? await loginService.VerifyPhoneCode(intCode, token) : new BadRequestResult();
                }

            }

            //User not authorized
            return Unauthorized();
        }

    }



}




