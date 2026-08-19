/* The host the Lua gates run scripts under.
 *
 * Built by the test suite from the vendored Lua sources (test/fixtures/tools/lua), this
 * file, and the generated tabbit_native.c - which makes the host the same shape a game
 * engine embedding Lua has: the interpreter compiled in, the native module registered
 * statically. Nothing is looked up on PATH.
 *
 * Usage: lua-host <script.lua> [args...]. The script sees the standard `arg` table.
 *
 * TABBIT_LUA_NO_NATIVE, when set, leaves tabbit.native unregistered - the gate for the
 * reader's promise that a project using neither encryption nor MAC needs no C module.
 */

#include <stdio.h>
#include <stdlib.h>

#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"

int luaopen_tabbit_native(lua_State* L);

int main(int argc, char** argv) {
  lua_State* L;
  int at;

  if (argc < 2) {
    fprintf(stderr, "usage: lua-host <script.lua> [args...]\n");
    return 1;
  }

  L = luaL_newstate();

  if (L == NULL) {
    fprintf(stderr, "cannot create a Lua state\n");
    return 1;
  }

  luaL_openlibs(L);

  if (getenv("TABBIT_LUA_NO_NATIVE") == NULL) {
    /* package.preload["tabbit.native"] = luaopen_tabbit_native - the static
     * registration a consumer without dynamic loading uses. */
    lua_getglobal(L, "package");
    lua_getfield(L, -1, "preload");
    lua_pushcfunction(L, luaopen_tabbit_native);
    lua_setfield(L, -2, "tabbit.native");
    lua_pop(L, 2);
  }

  /* The standard arg table: arg[0] is the script, arg[1..] what follows it. */
  lua_createtable(L, argc - 2, 1);

  for (at = 1; at < argc; ++at) {
    lua_pushstring(L, argv[at]);
    lua_rawseti(L, -2, at - 1);
  }

  lua_setglobal(L, "arg");

  if (luaL_dofile(L, argv[1]) != LUA_OK) {
    fprintf(stderr, "%s\n", lua_tostring(L, -1));
    lua_close(L);
    return 1;
  }

  lua_close(L);
  return 0;
}
