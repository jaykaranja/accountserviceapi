﻿using accountservice.Commons;
using accountservice.Controllers;
using accountservice.ForcedModels;
using accountservice.Interfaces;
using accountservice.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.Collections;
using System.Data;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
namespace accountservice.Implementations
{


    public class Login : ILogin
    {
        public const string APP_ADDRESS = "http://ibusinessaccountservice.azurewebsites.net/";
        //public const string APP_ADDRESS = "http://192.168.1.200:3000/";

        private readonly IConfiguration _config;
        private readonly HttpContext _httpContext;
        private readonly IDataProtectionProvider _idp; //For data protection such as encrypting and decryptin jwt token

        public Login(IConfiguration config, HttpContext httpContext, IDataProtectionProvider idp)
        {
            _config = config;
            _httpContext = httpContext;
            _idp = idp;

        }


        public async Task<IActionResult> LoginwithMicrosoft(string? code, string loginurl)
        {

            if (code == null)
            {
                //Generate other values such as status and status code
                string microsoft_login_url = _config.GetSection("Microsofturl").Get<string>();// "baseurl"
                //microsoft_login_url = "http://localhost:3000/sign-in"; //microsoft_login_url.Replace("baseurl", loginurl);

                return new RedirectResult(microsoft_login_url);
            }
            else
            {

                //process results
                using var client = new HttpClient();
                client.BaseAddress = new Uri("https://login.microsoftonline.com");

                Dictionary<string, string> stringbody = new Dictionary<string, string>
                {
                    { "code", code },
                    { "scope", "openid User.Read" },
                    { "client_id", _config["AzureAd:ClientId"]?? "no client id" },
                    { "redirect_uri", "http://localhost:3000/sign-in" },
                    { "grant_type", "authorization_code" },
                    { "client_secret", _config["AzureAd:ClientSecret"]??"nosecret key" }
                };

                var content = new FormUrlEncodedContent(stringbody);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

                HttpResponseMessage res = await client.PostAsync("/common/oauth2/v2.0/token", content);


                if (res.IsSuccessStatusCode)
                {

                    // ADJson? js = await res.Content.ReadFromJsonAsync<ADJson>();
                    string resBody = await res.Content.ReadAsStringAsync();
                    //JObject jObject = JObject.Parse(resBody);
                    //string? jwttoken = jObject["access_token"]?.Value<string>();

                    dynamic? obj = JsonConvert.DeserializeObject(resBody);
                    string? accesstoken = obj?.access_token;

                    //Using obtained token, extract values and check whether the user already exists.
                    //Otherwise get user information using Microsoft graph then register 'em


                    using (var client2 = new HttpClient())
                    {
                        client2.BaseAddress = new Uri("https://graph.microsoft.com");

                        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accesstoken);

                        res = await client2.GetAsync("/v1.0/me");

                        resBody = await res.Content.ReadAsStringAsync();


                        //Deserialize graph info
                        MUserMicrosoft? userGraphInfo = JsonConvert.DeserializeObject<MUserMicrosoft>(resBody);


                        Hashtable values = new Hashtable();


                        //use passed in email to get user information
                        //Get User record if exists in this application database
                        try
                        {
                            DatabaseHandler dbhander = DatabaseHandler.GetDAtabaseHandlerInstance();

                            Parameter[] parameters = new Parameter[]
                            {
                                new Parameter
                                {
                                    Name = "userName",
                                    Value = " ",
                                    Type = SqlDbType.NVarChar
    
                                },
                                new Parameter
                                {
                                    Name = "email",
                                    Value = userGraphInfo?.UserPrincipalName ?? "empty",
                                    Type = SqlDbType.NVarChar

                                }

                            };

                            dbhander.Parameters.AddRange(parameters);

                            using (SqlDataReader reader = await dbhander.ExecuteProcedure(_config.GetConnectionString("connString"), "spSelectUser"))
                            {

                                if (reader.HasRows)
                                {
                                    reader.Read();


                                    //Create a user principal. I.e login user
                                    //Check if password match then return ok
                                    //Create a user model
                                    MUser loggedINUser = new MUser()
                                    {
                                        UserID = reader.GetInt64(0),
                                        Email = reader.GetString(2),
                                        UserName = reader.GetString(1),
                                        FullName = reader.GetString(3),
                                        Telephone = reader.GetString(5),
                                        PhysicalAddress = reader.GetString(4),
                                        OriginCountry = reader.GetString(6),
                                        EmployerName = " " + reader.GetString(7),
                                        Experience = reader.GetInt32(8),
                                        Position = reader.GetString(9),
                                        DisabilityStatus = reader.GetString(10),
                                        Password = " "

                                    };

                                    //Ensure that user is fully registered otherwise proceed
                                    bool fullyRegistered = await isFullyRegistered(userGraphInfo?.Id ?? "");
                                    if (fullyRegistered)
                                    {
                                        //Now we create relevant logged in user
                                        values.Clear(); //Reset hash table values just incase

                                        values = genetrateToken(loggedINUser);


                                        return new OkObjectResult(values);
                                    }
                                    else
                                    {
                                        //Redirect to a detailed registration
                                        string registrationToken = FullRegistrationToken(userGraphInfo?.UserPrincipalName ?? "", userGraphInfo?.Id ?? "");
                                        //update graph info as saved in the database

                                        userGraphInfo.DisplayName = loggedINUser.FullName;
                                        userGraphInfo.GivenName = loggedINUser.UserName;
                                        userGraphInfo.MobilePhone = loggedINUser.Telephone;

                                        IDictionary<string, object> returnvalues = new Dictionary<string, object>
                                        {
                                            {"token", registrationToken },
                                            {"graphinfo", userGraphInfo},
                                            {"oauthprovider", "Microsoft" },
                                            {"post_to", "/login/loginwithmicrosoft" },
                                            {"registered", false }
                                            

                                        };

                                        return new OkObjectResult(returnvalues);

                                    }


                                }
                                else //First time login with microsoft. Register first
                                {
                                    string registrationToken = FullRegistrationToken(userGraphInfo?.UserPrincipalName ?? "", userGraphInfo?.Id ?? "");
                                    IDictionary<string, object> returnvalues = new Dictionary<string, object>
                                    {
                                        {"token", registrationToken },
                                        {"graphinfo", userGraphInfo??new() },
                                        {"oauthprovider", "Microsoft" },
                                        {"post_to", "/login/loginwithmicrosoft/" },
                                        {"registered", false }

                                    };

                                    return new OkObjectResult(returnvalues);

                                }
                            }    

                        }
                        catch (Exception e)
                        {
                            values.Add("Message", e.Message);//"Error trying to process your request"
                            values.Add("Success", false);

                            return new BadRequestObjectResult(values)
                            {
                                StatusCode = 408
                            };

                        }

                    }




                    //return new RedirectResult("http://localhost:3000/login#access_token=" + jwttoken);//Redirect to react app
                }

                else
                {
                    string con = await res.Content.ReadAsStringAsync();
                    return new BadRequestObjectResult(con);
                }
            }

        }

        /// <summary>
        /// A public accessible method obtain user information. Verify authenticity of the user via the supplied token 
        /// 
        /// The create their information in the system
        /// 
        /// </summary>
        /// <param name="user"> user information</param>
        /// <param name="auth_token">authorization token. Generated when a user tries to 
        /// sign in using microsoft/oauth provider for the first time
        /// </param>
        /// <param name="phonecode">optional phone number. generated when a user provide a phone number 
        /// during registration process
        /// </param>
        /// <returns>
        /// Returns an action result depending on the authenticy of user initiating this request
        /// </returns>
        public async Task<IActionResult> HandleOAuthUserRegistration(MUser user, string auth_token, int? phonecode)
        {
            List<Claim> tokenClaims = new List<Claim>();
            Jwt tokencredential = CommonMethods.GetJWTinfo(_config);

            

            //Verify the token before proceding with user registration. 
            //Because we use token claims to register the user
            if (CommonMethods.VerifyEncyptedJwtToken(auth_token, tokencredential.Key, out tokenClaims, tokencredential.Issuer, tokencredential.Audience, _config))
            {
                //Get user emal/principal name and user Oauth id from id. Use the information to add user to database
                string? userprincipalname = CommonMethods.getClaimValue("principalName", tokenClaims);
                string? immutableID = CommonMethods.getClaimValue("userid", tokenClaims);

                if(!(userprincipalname?.Trim() ==  string.Empty))//Register user using their email address
                {
                    user.Email = userprincipalname??"";
                    MUser? registerUser = await RegisterUser(user, "Microsoft");

                    if (registerUser != null)//If success user was successfully created. Update DB(Oauth user table) then log them in
                    {

                        //Update Oauth user information
                        bool oaut_info_osuccess = await updateOauthUserDbInformation(true, VerifyPhoneCode(phonecode ?? 0, tokenClaims).Result, "microsoft", immutableID);
                        if(oaut_info_osuccess)
                            return new OkObjectResult(genetrateToken(registerUser));
                    }

                    //Something critical happened. This activity should be logged
                    return new UnprocessableEntityObjectResult("Error occured processing your request");
                    


                }

            }

            //Happens when something went wrong with user information.
            //That is token is invalid or
            //claims are invalid

            return new UnauthorizedResult();
            
        }

        private async Task<bool> isFullyRegistered(string userOAuthId)
        {
            bool fullyRegistered = false;


            //Get OAuth registration details
            try
            {
                DatabaseHandler database = DatabaseHandler.GetDAtabaseHandlerInstance();

                database.Parameters.Add
                    (new Parameter { Name= "AuthUserID", Type = SqlDbType.NVarChar, Value = userOAuthId });

                using(SqlDataReader reader = await database.ExecuteProcedure(_config.GetConnectionString("connString"), "spSelectOAuthUser"))
                {
                    if (reader.HasRows)
                    {
                        await reader.ReadAsync();

                        fullyRegistered = ((int)reader.GetByte(0)) == 1; //User extra data were successfuly obtained and database updated
                    }
                }

            }
            catch (Exception)
            {
                //Handle any possible exception
                fullyRegistered = false;
            }


                return fullyRegistered;
        }



        /// <summary>
        /// Generates a JWT when provided a list of claims
        /// Returns a string token
        /// </summary>
        /// <param name="claims">A list of claims</param>
        /// <returns>string token</returns>
        private string generateClaimsToken(List<Claim> claims, double validity)
        {
            string token = string.Empty;

            Jwt jwt = CommonMethods.GetJWTinfo(_config);


            SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));

            SigningCredentials signin = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            JwtSecurityToken securityToken = new JwtSecurityToken
            (
                jwt.Issuer,
                jwt.Audience,
                claims,
                expires: DateTime.Now.AddHours(validity), //Token expires in one hour of in activities
                signingCredentials: signin
            );

            token = new JwtSecurityTokenHandler().WriteToken(securityToken);

            return token;
        }

        /// <summary>
        /// Generates a JWT when provided a list of claims
        /// Returns a string token
        /// </summary>
        /// <param name="claims">A list of claims</param>
        /// <returns>string token</returns>
        private string generateClaimsTokenEncrypted(List<Claim> claims, double validity)
        {
            string plainToken = generateClaimsToken(claims, validity);
            

            return new AesEncryption(_config).Encrypt(plainToken);
        }

        //A private function used to generate user JWT token per logged in user used across
        //All our API
        /// <summary>
        /// Generates a hashtable containing user information, their token, a status result
        /// </summary>
        /// <param name="loggedINUser">A successfully authenticated user information</param>
        /// <returns>A hashtable containing userinfo, their token, and status result</returns>
        private Hashtable genetrateToken(MUser loggedINUser)
        {
            Hashtable userinfo = new Hashtable();
            //Now we create relevant logged in user
            //Creating a Jwt object
            

            //Add relevant claims
            List<Claim> claims = new List<Claim>()
                                    {
                                        new Claim(JwtRegisteredClaimNames.Sub, _config["Jwt:Subject"]),
                                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                                        new Claim(JwtRegisteredClaimNames.Iat, DateTime.Now.ToString()),

                                        new Claim(ClaimTypes.PrimarySid, ""+ loggedINUser.UserID), //User id as identified by database
                                        new Claim(ClaimTypes.GivenName, loggedINUser.UserName??""), //Username. 
                                        new Claim(ClaimTypes.Country,loggedINUser.OriginCountry), //Country 
                                        new Claim(ClaimTypes.Name, loggedINUser.FullName),
                                        new Claim(ClaimTypes.Email, loggedINUser.Email),
                                        
                                        //Source of token indicator
                                        new Claim("LocalToken", "Yes")

                                        //And many more

                                    };

            //Most importantly. If username is admin add that claim to enable admin access
            if (loggedINUser.UserName?.ToLower() == "admin")
            {
                claims.Add(new Claim(MUser.ADMIN_TYPE, loggedINUser.UserName.ToLower()));
            }

            userinfo.Add("registered", true);
            userinfo.Add("token", generateClaimsTokenEncrypted(claims, 10)); //10 hrs for a logged in token
            userinfo.Add("user", loggedINUser);

            return userinfo;
        }


        /// <summary>
        /// Used to generate a token that is used to collect user specific iformation for a complete registration
        /// embedes principal name provided by Oauth provideer and immutable id that can be used later 
        /// to verify a user before adding their details to the database
        /// </summary>
        /// <param name="principalname">Principal name as provided by choosen OAuth provider</param>
        /// <param name="immutableID">immutable id assigned to this user by OAuth provider</param>
        /// <returns></returns>
        private string FullRegistrationToken(string principalname, string immutableID)
        {
            //Generate a short timed token to help handle data collection for this user
            //Specific stored in the token as claims
            // the information include user id (email)
            List<Claim> claims = new List<Claim>()
                                    {
                                        new Claim(JwtRegisteredClaimNames.Sub, _config["Jwt:Subject"]),
                                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                                        new Claim(JwtRegisteredClaimNames.Iat, DateTime.Now.ToString()),

                                        new Claim("principalName", principalname), //User principal id
                                        new Claim("userid", "" + immutableID) 
                                        
                                        //And many more

                                    };


            return generateClaimsTokenEncrypted(claims, 0.16667); //0.1667 for approximately ten minutes to complete the whole process. Unless start zero
 
        }

        private async Task<bool> VerifyPhoneCode(int code, List<Claim> claims)
        {
            

            string phoneNumber = CommonMethods.getClaimValue("phoneNumber", claims)??"";

            //Get phone code from cosmos db
            //Get phonecode from the database
            CosmosDbHandler<MUserPhoneVerification> handler = CosmosDbHandler<MUserPhoneVerification>.CreateCosmosHandlerInstance("purchaseorderitems", "phoneverification");

            try
            {
                string query = $"SELECT * FROM c WHERE c.phoneNumber = '{phoneNumber}' and c.VerificationCode = {code}";

                List<MUserPhoneVerification> verification = await handler.QuerySelector(query);

                //If success update db then return
                if(verification.Count == 1 && verification[0].ExpiryEpoch > DateTime.Now.Ticks)
                {

                }

                return true;
                
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// Generates a phone code. for the provided phone number. Save in a secure fault then return a newly manufactured 
        /// Token containing a phone number
        /// </summary>
        /// <param name="phoneNumber"></param>
        /// <param name="claims"></param>
        /// <param name="token"></param>
        /// <returns> An action with the token or not authorized result</returns>
        public async Task<IActionResult> GeneratePhoneCode(string phoneNumber, string token)
        {
            //First we verify token provided
            Jwt tokencredential = CommonMethods.GetJWTinfo(_config);

            List<Claim> claims;
            if (CommonMethods.VerifyEncyptedJwtToken(token, tokencredential.Key, out claims, tokencredential.Issuer, tokencredential.Audience, _config))
            {
                Random rnd = new Random();
                int code = rnd.Next(100000, 999999);
                claims.Add(new Claim("phoneNumber", phoneNumber));

                //Get time that was to generate this token it will be same for the newly generated token
                int seconds;
                int.TryParse(CommonMethods.getClaimValue("exp", claims) ?? "0", out seconds);
                double hoursPan = (DateTime.UnixEpoch.AddSeconds(seconds) - DateTime.UtcNow).Hours;
                hoursPan = hoursPan < 0 ? 0.1667 : hoursPan; //Incase time expires but we are here, add 10 minutes

                //Send the code using preffered SMS API provider
                //As well as save it in to the cosmos db
                //Return information 
                Dictionary<string, string> res = new Dictionary<string, string>
                {
                    { "token", generateClaimsTokenEncrypted(claims, hoursPan)}, //Pass remaining hours of the original token
                    { "phoneNumber", phoneNumber }
                };   
                try
                {
                    //Send the code to user before saving it to the database
                    //happens asychronously. Response is immaterial
                    await CommonMethods.sendOtpMessage(phoneNumber, code).ConfigureAwait(false);

                    //Save it to the database
                    CosmosDbHandler<MUserPhoneVerification> dbHandler = CosmosDbHandler<MUserPhoneVerification>.CreateCosmosHandlerInstance("purchaseorderitems", "phoneverification");
                   
                    MUserPhoneVerification item = new MUserPhoneVerification
                    { phoneNumber = phoneNumber, ExpiryEpoch = DateTime.Now.AddMinutes(10).Ticks, VerificationCode = code };

                    bool result = await dbHandler.InsertItem(item, item.phoneNumber);

                    if(result)
                        return new OkObjectResult(res);
                }
                catch(Exception ex)
                {
                    res.Clear();
                    res.Add("status", "" + false);
                    res.Add("message", ex.Message);

                    return new BadRequestObjectResult(res);
                }

            }
            //Unauthorized user and other malformed requests not handled above
            {
                return new UnauthorizedResult();
            }
        }

        private async Task<MUser?> RegisterUser(MUser user, string modeused)
        {
            Register userRegister = new Register(_config);

            RegistrationResult registrationResult = await userRegister.RegisterUser(user, modeused);

            if(registrationResult.Status)
                return user;
           
                
            return null;

        }


        /// <summary>
        /// 
        /// Updates a specific user Oauth information
        /// This information is used to track whether user who logged in using OAuth provider were fully registered
        /// </summary>
        /// <returns></returns>
        private async Task<bool> updateOauthUserDbInformation(bool registrationconfirmed, bool phoneverified, string? provider, string? oauthid)
        {
            string defaultDate = new DateTime(1971, 1, 1).ToString();

            string datePhoneVerified = phoneverified ? DateTime.Now.ToString() : defaultDate;
            string dateRegistrationConfirmed = registrationconfirmed ? DateTime.Now.ToString() : defaultDate;

            DatabaseHandler db = DatabaseHandler.GetDAtabaseHandlerInstance();



            Parameter[] parameters = 
            {
                new Parameter
                {
                    Name = "RegistrationConfirmed",
                    Type = SqlDbType.TinyInt,
                    Value = "" + (registrationconfirmed ? 1 : 0)

                },
                new Parameter
                {
                    Name = "PhoneVerified",
                    Type = SqlDbType.TinyInt,
                    Value = "" + (phoneverified ? 1 : 0)
                },
                new Parameter
                {
                    Name = "DateRegistrationConfirmed",
                    Type = SqlDbType.NVarChar,
                    Value = dateRegistrationConfirmed
                },
                new Parameter
                {
                    Name = "DatePhoneVerified",
                    Type = SqlDbType.NVarChar,
                    Value = datePhoneVerified
                },
                  new Parameter
                   {
                        Name = "Provider",
                        Type = SqlDbType.NVarChar,
                        Value = provider

                   },
                  new Parameter
                  {
                      Name = "AuthUserID",
                      Type = SqlDbType.NVarChar,
                      Value = oauthid
                  }
            };

            db.Parameters.AddRange(parameters);

            using(SqlDataReader? reader =await db.ExecuteProcedure(_config.GetConnectionString("connString"), "spInsertUpdateAOuthUser"))
            {
                if (reader?.HasRows ?? false)
                {
                    reader.Read();

                    return reader.GetInt32(0) == 1;
                }
                


                db.CloseResources();
            }
            
            return false;
        }

        public async Task<IActionResult> VerifyPhoneCode(int code, string token)
        {

            //Verify authenticity of the token, then retrive token in it
            //First we verify token provided
            Jwt tokencredential = CommonMethods.GetJWTinfo(_config);

            List<Claim> claims;
            if (CommonMethods.VerifyEncyptedJwtToken(token, tokencredential.Key, out claims, tokencredential.Issuer, tokencredential.Audience, _config))
            {
                bool isPhoneVefiried = await VerifyPhoneCode(code, claims);

                if (isPhoneVefiried)
                {
                    IDictionary<string, string> response = new Dictionary<string, string>
                    {
                        {"status", ""+true },
                        {"message", "Phone verified successfully" }
                    };

                    return new OkObjectResult(response);
                }

            }
            //Handles unauthorized request and any other bad requests
            return new UnauthorizedObjectResult("Error occured processing your request");
        }

    }
}
