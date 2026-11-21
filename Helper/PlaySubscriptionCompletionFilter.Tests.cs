using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Helper;

#pragma warning disable CS1591
public class PlaySubscriptionCompletionFilterTests
{
    [Test]
    public async Task ReserveQuota_EnforcesBothRollingWindowsWithoutWritingOnRejection()
    {
        using var lua = StartLua();
        if (lua == null)
            Assert.Ignore("Lua is unavailable. This test executes the production script with simulated Redis commands.");

        try
        {
            var errors = lua.StandardError.ReadToEndAsync();
            await lua.StandardInput.WriteLineAsync("local function reserveQuota()");
            await lua.StandardInput.WriteLineAsync(PlaySubscriptionCompletionFilter.ReserveQuota);
            await lua.StandardInput.WriteLineAsync("end");
            await lua.StandardInput.WriteAsync(QuotaCases);
            lua.StandardInput.Close();
            await lua.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(lua.ExitCode, Is.Zero, await errors);
        }
        finally
        {
            if (!lua.HasExited)
                lua.Kill();
        }
    }

    private static Process StartLua()
    {
        foreach (var executable in new[] { "lua5.1", "lua" })
        {
            try
            {
                return Process.Start(new ProcessStartInfo(executable, "-")
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                // Try the next interpreter name when this executable is missing.
            }
        }
        return null;
    }

    private const string QuotaCases = """
        local lists, expires, writes, now = {}, {}, 0, 100000
        redis = {call = function(command, key, first, last)
            if command == 'TIME' then
                return {tostring(math.floor(now / 1000)), tostring(now % 1000 * 1000)}
            end
            if expires[key] and expires[key] <= now then
                lists[key], expires[key] = nil, nil
            end
            local list = lists[key] or {}
            if command == 'LINDEX' then return list[#list + first + 1] or false end
            writes = writes + 1
            if command == 'RPUSH' then
                list[#list + 1] = tostring(first)
                lists[key] = list
            elseif command == 'LTRIM' then
                assert(last == -1)
                for i = 1, #list + first do table.remove(list, 1) end
            elseif command == 'PEXPIRE' then
                expires[key] = now + first
            else
                error('unexpected Redis command ' .. command)
            end
        end}

        local function check(client, expected)
            KEYS = {client, 'global'}
            local before = writes
            local result = reserveQuota()
            assert(result == expected, client .. ': expected ' .. expected .. ', got ' .. result)
            if expected > 0 then
                assert(writes == before, 'rejection must not write or refresh either key')
            else
                assert(expires[client] == now + 60000 and expires.global == now + 60000,
                    'client and global keys must expire after one minute')
            end
        end

        check('staggered', 0)
        now = 101500
        for i = 1, 9 do check('staggered', 0) end
        now = 159999
        check('staggered', 1)
        now = 160000
        check('staggered', 0)
        assert(#lists.staggered == 10)
        check('staggered', 2)

        lists, expires, now = {}, {}, 100000
        check('first', 0)
        now = 101500
        for i = 1, 10 do check('frequent', 0) end
        for i = 1, 989 do check('buyer-' .. i, 0) end
        now = 102001
        check('frequent', 60) -- The client wait wins over the global wait of 58 seconds.
        check('fresh', 58)
        assert(lists.fresh == nil)
        now = 159999
        check('fresh', 1)
        now = 160000
        check('fresh', 0)
        assert(#lists.global == 1000)
        check('another', 2)
        now = 161500
        check('frequent', 0)
        assert(#lists.frequent == 1)
        """;
}
#pragma warning restore CS1591
