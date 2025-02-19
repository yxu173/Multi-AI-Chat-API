using Domain.Aggregates.Users;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<User?> _userManager;

    public UserRepository(ApplicationDbContext context, UserManager<User?> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IdentityResult> CreateAsync(User user, string password)
    {
        return await _userManager.CreateAsync(user, password);
    }

    public async Task DeleteAsync(Guid id)
    {
        var user = GetByIdAsync(id).Result;
        await _userManager.DeleteAsync(user);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _userManager.FindByIdAsync(id.ToString()) != null;
    }

    public async Task<bool> ExistsByEmailAsync(string email)
    {
        return await _userManager.FindByEmailAsync(email) != null;
    }

    public async Task<bool> ExistsByUsernameAsync(string username)
    {
        return await _userManager.FindByNameAsync(username) != null;
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        return await _context.Users.ToListAsync();
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _userManager.FindByEmailAsync(email);
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _userManager.FindByIdAsync(id.ToString());
    }

    public async Task<User> GetByUsernameAsync(string username)
    {
        return await _userManager.FindByNameAsync(username);
    }

    public async Task<IEnumerable<User?>> GetUsersByRoleAsync(string role)
    {
        return await _userManager.GetUsersInRoleAsync(role);
    }

    public async Task<bool> IsEmailUniqueAsync(string email)
    {
        return await _userManager.FindByEmailAsync(email) == null;
    }

    public async Task<bool> IsUsernameUniqueAsync(string username)
    {
        return await _userManager.FindByNameAsync(username) == null;
    }

    public Task<string> GeneratePasswordResetTokenAsync(User user)
    {
        return _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<bool> ResetPasswordAsync(User user, string token, string newPassword)
    {
         var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
         if (!result.Succeeded)
             return false;
         return true;
    }

    public async Task<bool> CheckPasswordAsync(User user, string password)
    {
        return await _userManager.CheckPasswordAsync(user, password);
    }

    public async Task UpdateAsync(User? user)
    {
        await _userManager.UpdateAsync(user);
        await _context.SaveChangesAsync();
    }
}