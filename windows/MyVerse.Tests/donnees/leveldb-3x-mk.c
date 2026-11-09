// Crée une base LevelDB façon « Local Storage » de Chromium pour tester la reprise des données 3.x.
#include <leveldb/c.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static leveldb_t *db; static leveldb_writeoptions_t *wo; static char *err = NULL;
static void put(const char *k, size_t kl, const char *v, size_t vl) {
  leveldb_put(db, wo, k, kl, v, vl, &err); if (err) { fprintf(stderr, "%s\n", err); exit(1); }
}
// clé : "_" + origine + "\0" + 0x01 + nom (Latin-1)
static size_t cle(char *o, const char *nom) {
  const char *pre = "_http://127.0.0.1:47836"; size_t n = strlen(pre);
  memcpy(o, pre, n); o[n++] = 0; o[n++] = 1; memcpy(o + n, nom, strlen(nom)); return n + strlen(nom);
}
// valeur UTF-16LE : 0x00 + texte (ASCII ou UTF-8 simple converti ici en UTF-16 sans accents complexes)
static size_t utf16(char *o, const char *s) {
  size_t n = 0; o[n++] = 0;
  const unsigned char *p = (const unsigned char *)s;
  while (*p) {
    unsigned cp;
    if (*p < 0x80) cp = *p++;
    else if ((*p & 0xE0) == 0xC0) { cp = ((p[0] & 0x1F) << 6) | (p[1] & 0x3F); p += 2; }
    else { cp = ((p[0] & 0x0F) << 12) | ((p[1] & 0x3F) << 6) | (p[2] & 0x3F); p += 3; }
    o[n++] = cp & 0xff; o[n++] = cp >> 8;
  }
  return n;
}
int main(int argc, char **argv) {
  leveldb_options_t *op = leveldb_options_create();
  leveldb_options_set_create_if_missing(op, 1);
  leveldb_options_set_compression(op, leveldb_snappy_compression);
  db = leveldb_open(op, argv[1], &err); if (err) { fprintf(stderr, "%s\n", err); return 1; }
  wo = leveldb_writeoptions_create();
  char k[256], *v = malloc(4 << 20); size_t kl, vl;
  // gros recueil de chants (répétitif : bien compressé par snappy) en UTF-16
  char *json = malloc(2 << 20); strcpy(json, "[");
  for (int i = 0; i < 2000; i++) {
    char un[300]; sprintf(un, "%s{\"id\":\"c%04d\",\"title\":\"%d. Grâce infinie’\",\"author\":\"Recueil\",\"sections\":[{\"label\":\"Couplet\",\"text\":\"Ligne une\\nLigne deux\"}]}", i ? "," : "", i, i + 1);
    strcat(json, un);
  }
  strcat(json, "]");
  kl = cle(k, "verselight-chants"); vl = utf16(v, json); put(k, kl, v, vl);
  kl = cle(k, "verselight-theme"); v[0] = 1; strcpy(v + 1, "\"aube\""); put(k, kl, v, 1 + strlen(v + 1));
  kl = cle(k, "myverse-journal"); v[0] = 1; strcpy(v + 1, "[\"x\"]"); put(k, kl, v, 1 + strlen(v + 1));
  put("META:http://127.0.0.1:47836", 27, "x", 1);
  leveldb_compact_range(db, NULL, 0, NULL, 0); // → table .ldb compressée
  // ensuite dans le journal (.log) : valeur remplacée et clé supprimée
  kl = cle(k, "verselight-theme"); v[0] = 1; strcpy(v + 1, "\"vert\""); put(k, kl, v, 1 + strlen(v + 1));
  kl = cle(k, "verselight-taille"); v[0] = 1; strcpy(v + 1, "1.2"); put(k, kl, v, 1 + strlen(v + 1));
  kl = cle(k, "verselight-presentation"); v[0] = 1; strcpy(v + 1, "\"bandeau\""); put(k, kl, v, 1 + strlen(v + 1));
  kl = cle(k, "verselight-presentation"); leveldb_delete(db, wo, k, kl, &err);
  kl = cle(k, "myverse-decoupes"); v[0] = 1; strcpy(v + 1, "{\"42.3.16\":[\"Car Dieu\",\"a tant aimé\"]}"); put(k, kl, v, 1 + strlen(v + 1));
  // pas de leveldb_close : on laisse le .log comme une 3.x encore ouverte (sinon il serait vidé dans une table)
  printf("ok\n");
  _exit(0);
}
