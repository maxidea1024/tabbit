/* Conformance harness for the generated C reader.
 *
 * Reads Vectors.tcb through the generated accessor and prints each row in the
 * canonical form described in ../README.md. No parsing here: the generated
 * reader does that, and this only prints.
 *
 * The header is named on the command line by the build, so this file works for
 * any scenario without knowing the accessor's name.
 */

#include TABBIT_ACCESSOR_HEADER

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* A JSON string. The corpus holds an empty value, a non-ASCII one and control
 * characters, so this escapes rather than assuming anything is printable.
 *
 * UTF-8 goes through as bytes: the reader hands back exactly what the exporter
 * wrote, and re-encoding it here would be this harness disagreeing with the
 * thing it is meant to be checking. */
static void print_quoted(const char* value)
{
    const unsigned char* at = (const unsigned char*)value;

    putchar('"');

    for (; *at != '\0'; ++at) {
        switch (*at) {
            case '"':  fputs("\\\"", stdout); break;
            case '\\': fputs("\\\\", stdout); break;
            case '\n': fputs("\\n", stdout); break;
            case '\r': fputs("\\r", stdout); break;
            case '\t': fputs("\\t", stdout); break;

            default:
                if (*at < 0x20)
                    printf("\\u%04x", (unsigned)*at);
                else
                    putchar((int)*at);

                break;
        }
    }

    putchar('"');
}

/* A double, with enough digits to survive the round trip.
 *
 * %.17g is what it takes for a double to read back identically; the float is
 * widened to one, which is exactly what the corpus comparison expects. */
static void print_number(double value)
{
    printf("%.17g", value);
}

/* An environment variable, or NULL.
 *
 * MSVC deprecates getenv and this harness is built with warnings as errors, so the
 * branch is the same one tb_fopen_read makes in the reader: use the platform's
 * replacement rather than turning the warning off for the whole translation unit.
 *
 * The buffer is freed nowhere on purpose - it lives as long as the process, which is
 * what the accessor's pointer into the key needs anyway. */
static const char* environment_value(const char* name)
{
#if defined(_MSC_VER)
    char* value = NULL;
    size_t length = 0;

    if (_dupenv_s(&value, &length, name) != 0)
        return NULL;

    return value;
#else
    return getenv(name);
#endif
}

/* One hexadecimal digit, or -1. Written out rather than reached for in the library
 * because the C gate compiles with warnings as errors, and the scanning functions are
 * the ones a Microsoft compiler refuses under that setting. */
static int hex_digit(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;

    return -1;
}

int main(int argc, char** argv)
{
    /* Zeroed, which is what LoadAll requires: it frees the previous load before
     * swapping in the new one, and on a first call there has to be nothing there. */
    ConformanceData_t data = {0};
    char error[512];
    int32_t row;
    const char* mac_key_text;
    static uint8_t mac_key[32];

    if (argc < 2) {
        fprintf(stderr, "usage: conformance-c <binary-directory>\n");
        return 1;
    }

    /* The corpus is signed, so the key goes in before the first read - which is the
     * whole of what a consuming project does about the MAC. Without it the files
     * would still load, and nothing here would notice: the check is the reader's,
     * and it needs the key to run.
     *
     * The buffer is static because the accessor keeps the pointer rather than a
     * copy, and it has to outlive every load. */
    mac_key_text = environment_value("TABBIT_TEST_TCB_MAC_KEY");

    if (mac_key_text != NULL && strlen(mac_key_text) == 64) {
        int at;

        for (at = 0; at < 32; ++at) {
            int high = hex_digit(mac_key_text[at * 2]);
            int low = hex_digit(mac_key_text[at * 2 + 1]);

            if (high < 0 || low < 0) {
                fprintf(stderr, "the MAC key in the environment is not hexadecimal\n");
                return 1;
            }

            mac_key[at] = (uint8_t)((high << 4) | low);
        }

        ConformanceData_MacKey = mac_key;
        ConformanceData_MacKeyLength = 32;
    }

    memset(error, 0, sizeof error);

    if (!ConformanceData_LoadAll(&data, argv[1], error, sizeof error)) {
        fprintf(stderr, "load failed: %s\n", error);
        return 1;
    }

    putchar('[');

    for (row = 0; row < data.vectors.count; ++row) {
        const ConformanceData_VectorsRecord_t* r = &data.vectors.records[row];
        char uuid[37];
        int32_t i;

        if (row > 0)
            putchar(',');

        printf("{\"index\":%d,", (int)r->index);
        printf("\"intVal\":%d,", (int)r->int_val);

        /* A string, because JSON's single numeric type would round anything
         * past 2^53. */
        printf("\"bigVal\":\"%lld\",", (long long)r->big_val);

        fputs("\"floatVal\":", stdout);
        print_number((double)r->float_val);

        fputs(",\"doubleVal\":", stdout);
        print_number(r->double_val);

        fputs(",\"text\":", stdout);
        print_quoted(r->text);

        printf(",\"flag\":%s,", r->flag ? "true" : "false");

        /* Ticks, which is what the generated fields hold. */
        printf("\"when\":\"%lld\",", (long long)r->when);
        printf("\"span\":\"%lld\",", (long long)r->span);

        tb_uuid_to_string(&r->uid, uuid);
        printf("\"uid\":\"%s\",", uuid);

        printf("\"label\":%d,", (int)r->label);

        fputs("\"ints\":[", stdout);
        for (i = 0; i < r->ints_count; ++i) {
            if (i > 0)
                putchar(',');

            printf("%d", (int)r->ints[i]);
        }

        fputs("],\"strs\":[", stdout);
        for (i = 0; i < r->strs_count; ++i) {
            if (i > 0)
                putchar(',');

            print_quoted(r->strs[i]);
        }

        /* The two array forms whose element read is not the scalar one in a loop. An enum
           element is the one place this target reads into a scratch int and casts. */
        fputs("],\"labels\":[", stdout);
        for (i = 0; i < r->labels_count; ++i) {
            if (i > 0)
                putchar(',');

            printf("%d", (int)r->labels[i]);
        }

        fputs("],\"uids\":[", stdout);
        for (i = 0; i < r->uids_count; ++i) {
            if (i > 0)
                putchar(',');

            tb_uuid_to_string(&r->uids[i], uuid);
            printf("\"%s\"", uuid);
        }

        /* The reference indices, which is what the exporter writes for a foreign field. */
        printf("],\"owner\":%d,\"tier\":%d,",
               (int)r->owner, (int)r->tier_index);

        /* And one reference per element, printed as the stored index each came in as. */
        fputs("\"owners\":[", stdout);
        for (i = 0; i < r->owners_count; ++i) {
            if (i > 0)
                putchar(',');

            printf("%d", (int)r->owners[i]);
        }

        fputs("],", stdout);

        /* The three the v104 encodings win on. */
        fputs("\"count\":", stdout);
        print_number(r->count);

        fputs(",\"route\":", stdout);
        print_quoted(r->route);

        fputs(",\"zone\":", stdout);
        print_quoted(r->zone);

        putchar('}');
    }

    putchar(']');

    ConformanceData_Free(&data);

    return 0;
}
