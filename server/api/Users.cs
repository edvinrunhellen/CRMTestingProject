using System.Data;
using System.Text.Json;
using Npgsql;
using server.Authorization;
using server.Classes;
using server.Enums;
using server.Records;

namespace server.api;

public class Users
{
    private NpgsqlDataSource Db;
    public Users(WebApplication app, NpgsqlDataSource db, string url)
    {
        Db = db;
        url += "/users";

        app.MapGet(url + "/bycompany", (Delegate)GetEmployeesByCompany).RoleAuthorization(Role.ADMIN);
        app.MapPost(url + "/admin", CreateAdmin);
        app.MapPost(url + "/create", CreateEmployee).RoleAuthorization(Role.ADMIN);
        app.MapPut(url + "/{userId}", UpdateUser).RoleAuthorization(Role.ADMIN);
        app.MapDelete(url + "/{userId}", DeleteUser).RoleAuthorization(Role.ADMIN);
    }

    async Task<IResult> GetEmployeesByCompany(HttpContext context)
    {
        var user = JsonSerializer.Deserialize<User>(context.Session.GetString("User"));

        List<Employee> employeesList = new List<Employee>();
        await using var cmd = Db.CreateCommand("SELECT * FROM users_with_company WHERE company_name = @company_name");
        cmd.Parameters.AddWithValue("@company_name", user.Company);

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (reader.HasRows)
            {
                while (await reader.ReadAsync())
                {
                    employeesList.Add(new Employee(
                        reader.GetInt32(reader.GetOrdinal("user_id")),
                        reader.GetString(reader.GetOrdinal("username")),
                        reader.IsDBNull(reader.GetOrdinal("firstname")) ? String.Empty : reader.GetString(reader.GetOrdinal("firstname")),
                        reader.IsDBNull(reader.GetOrdinal("lastname")) ? String.Empty : reader.GetString(reader.GetOrdinal("lastname")),
                        reader.GetString(reader.GetOrdinal("email")),
                        Enum.Parse<Role>(reader.GetString(reader.GetOrdinal("role")))
                    ));
                }
                return Results.Ok(new { employees = employeesList });
            }
        }
        return Results.NoContent();
    }

    async Task<IResult> CreateAdmin(RegisterRequest registerRequest)
    {
        // Hämta företagets ID baserat på det angivna företagsnamnet
        await using var cmd = Db.CreateCommand("SELECT id FROM companies WHERE name = @company LIMIT 1;");
        cmd.Parameters.AddWithValue("@company", registerRequest.Company);

        try
        {
            var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int companyId = reader.GetInt32(reader.GetOrdinal("id"));

                // Lägg till den nya användaren som admin i den befintliga företaget
                await using var cmd2 = Db.CreateCommand(
                    "INSERT INTO users (username, password, role, email, company) VALUES (@username, @password, 'ADMIN', @email, @company_id) RETURNING id;"
                );

                cmd2.Parameters.AddWithValue("@username", registerRequest.Username);
                cmd2.Parameters.AddWithValue("@password", registerRequest.Password);
                cmd2.Parameters.AddWithValue("@email", registerRequest.Email);
                cmd2.Parameters.AddWithValue("@company_id", companyId);

                try
                {
                    var reader2 = await cmd2.ExecuteReaderAsync();
                    if (await reader2.ReadAsync()) // Kontrollera om användaren skapades korrekt
                    {
                        int userId = reader2.GetInt32(reader2.GetOrdinal("id"));

                        return Results.Ok(new
                        {
                            message = "User (admin) created successfully.",
                            userId,
                            company = registerRequest.Company,
                            email = registerRequest.Email,
                            role = "ADMIN"
                        });
                    }
                }
                catch
                {
                    return Results.Conflict(new { message = "User already exists." });
                }
            }
            else
            {
                return Results.NotFound(new { message = "Company not found." });
            }
        }
        catch
        {
            return Results.Conflict(new { message = "Something went wrong while processing the request." });
        }

        return Results.Json(new { message = "Something went wrong." }, statusCode: 500);
    }
    async Task<IResult> CreateEmployee(HttpContext context, CreateEmployeeRequest createEmployeeRequest)
    {
        try
        {
            var user = JsonSerializer.Deserialize<User>(context.Session.GetString("User"));

            await using var cmd = Db.CreateCommand("SELECT * FROM companies WHERE name = @company");
            cmd.Parameters.AddWithValue("@company", user.Company);

            var reader = await cmd.ExecuteScalarAsync();
            if (reader != null)
            {
                // Här ändrar vi till att använda RETURNING id för att få användar-ID
                await using var cmd2 = Db.CreateCommand("INSERT INTO users (firstname, lastname, username, password, role, email, company) " +
                                                        "VALUES (@firstname, @lastname, @username, @password, @role::role, @email, @company_id) RETURNING id;");
                cmd2.Parameters.AddWithValue("@firstname", createEmployeeRequest.Firstname);
                cmd2.Parameters.AddWithValue("@lastname", createEmployeeRequest.Lastname);
                cmd2.Parameters.AddWithValue("@username", createEmployeeRequest.Firstname + "_" + createEmployeeRequest.Lastname);
                cmd2.Parameters.AddWithValue("@password", createEmployeeRequest.Password);
                cmd2.Parameters.AddWithValue("@role", Enum.Parse<Role>(createEmployeeRequest.Role).ToString());
                cmd2.Parameters.AddWithValue("@email", createEmployeeRequest.Email);
                cmd2.Parameters.AddWithValue("@company_id", reader);

                try
                {
                    // Vi använder ExecuteReaderAsync för att få id från den just skapade användaren
                    await using (var reader2 = await cmd2.ExecuteReaderAsync())
                    {
                        if (await reader2.ReadAsync())
                        {
                            int userId = reader2.GetInt32(reader2.GetOrdinal("id"));

                            // Lägg till loggmeddelande för att kontrollera om användar-ID:t returnerades korrekt
                            Console.WriteLine($"Created user with ID: {userId}");

                            return Results.Ok(new
                            {
                                message = "User registered.",
                                userId,  // Lägg till userId i svaret
                                company = user.Company,
                                email = createEmployeeRequest.Email,
                                role = createEmployeeRequest.Role
                            });
                        }
                        else
                        {
                            // Om inget användar-ID returnerades, skriv ut ett meddelande
                            Console.WriteLine("No user was created, no rows returned.");
                            return Results.Conflict(new { message = "Query executed but no user was created." });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Fånga eventuella fel vid skapande och logga
                    Console.WriteLine($"Error: {ex.Message}");
                    return Results.Conflict(new { message = "User already exists" });
                }
            }
            else
            {
                return Results.NotFound(new { message = "Company not found." });
            }
        }
        catch
        {
            return Results.Json(new { message = "Something went wrong." }, statusCode: 500);
        }
    }

    async Task<IResult> UpdateUser(int userId, HttpContext context, UpdateUserRequest updateUserRequest)
    {
        var user = JsonSerializer.Deserialize<User>(context.Session.GetString("User"));

        await using var cmd = Db.CreateCommand("UPDATE users SET firstname = @firstname, lastname = @lastname, email = @email, role = @role::role WHERE id = @user_id AND company = @company_id;");
        cmd.Parameters.AddWithValue("@firstname", updateUserRequest.Firstname);
        cmd.Parameters.AddWithValue("@lastname", updateUserRequest.Lastname);
        cmd.Parameters.AddWithValue("@email", updateUserRequest.Email);
        cmd.Parameters.AddWithValue("@role", Enum.Parse<Role>(updateUserRequest.Role).ToString());
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@company_id", user.CompanyId);

        try
        {
            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 1)
            {
                return Results.Ok(new { message = "User updated successfully." });
            }
            else
            {
                return Results.Conflict(new { message = "Query executed but something went wrong." });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Results.Conflict(new { message = "User update failed." });
        }
    }

    async Task<IResult> DeleteUser(int userId, HttpContext context)
    {
        var user = JsonSerializer.Deserialize<User>(context.Session.GetString("User"));


        await using var cmd = Db.CreateCommand("DELETE FROM users WHERE id = @user_id AND company = @company_id;");
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@company_id", user.CompanyId);

        try
        {
            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected == 1)
            {
                return Results.Ok(new { message = "User was deleted successfully." });
            }
            else if (rowsAffected == 0)
            {
                return Results.NotFound(new { message = "No user was found." });
            }
            else
            {
                return Results.Conflict(new { message = "Query executed but something went wrong." });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return Results.Conflict(new { message = "Query was not executed." });
        }
    }

}