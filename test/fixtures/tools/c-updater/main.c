/* Drives one update and prints what it did, for the C# test to assert against.
 *
 * The updater under test is the shipped one - lib/c/tabbit/tabbit_updater.h -
 * compiled beside this file exactly as a consumer's build would compile it, with
 * the same one link flag: -lcurl.
 *
 * The MD5 vectors are printed too. They are the published ones, and they are the
 * only reading of the sixty-four constants in that file that counts. */

#include <stdio.h>
#include <string.h>

#include "tabbit/tabbit_updater.h"

static void print_line(void* context, const char* message) {
  (void)context;
  fprintf(stderr, "%s\n", message);
}

/* A JSON string. Enough escaping for a path and a message. */
static void print_quoted(const char* value) {
  size_t at;

  if (value == NULL || value[0] == '\0') {
    printf("null");
    return;
  }

  putchar('"');

  for (at = 0; value[at] != '\0'; ++at) {
    char c = value[at];

    switch (c) {
      case '"': printf("\\\""); break;
      case '\\': printf("\\\\"); break;
      case '\n': printf("\\n"); break;
      case '\r': printf("\\r"); break;
      case '\t': printf("\\t"); break;
      default:
        if ((unsigned char)c < 0x20)
          printf("\\u%04x", (unsigned)(unsigned char)c);
        else
          putchar(c);
    }
  }

  putchar('"');
}

int main(int argc, char** argv) {
  tb_update_options options;
  tb_update_result result;

  if (argc < 3) {
    fprintf(stderr, "usage: c-updater <base-url> <cache-directory>\n");
    return 2;
  }

  {
    char digest[33];
    const char* fox = "The quick brown fox jumps over the lazy dog";

    tb_md5_hex((const uint8_t*)"abc", 3, digest);
    printf("{\"md5abc\":\"%s\"", digest);

    tb_md5_hex((const uint8_t*)"", 0, digest);
    printf(",\"md5empty\":\"%s\"", digest);

    tb_md5_hex((const uint8_t*)fox, strlen(fox), digest);
    printf(",\"md5fox\":\"%s\"}\n", digest);
  }

  tb_update_options_init(&options);

  /* Short, because the retry test would otherwise spend its time asleep. */
  options.retry_delay_ms = 50;
  options.log = print_line;

  tb_update(argv[1], argv[2], &options, &result);

  printf("{\"succeeded\":%s", result.succeeded ? "true" : "false");
  printf(",\"error\":");
  print_quoted(result.error);
  printf(",\"upToDate\":%s", result.up_to_date ? "true" : "false");
  printf(",\"downloadedCount\":%d", (int)result.downloaded_count);
  printf(",\"downloadedBytes\":%lld", (long long)result.downloaded_bytes);
  printf(",\"deletedCount\":%d", (int)result.deleted_count);
  printf(",\"localPath\":");
  print_quoted(argv[2]);
  printf("}\n");

  return 0;
}
