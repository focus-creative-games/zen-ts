/* Minimal <unistd.h> for MSVC QuickJS libc build (ZTS). */
#pragma once
#include <io.h>
#include <process.h>
#include <direct.h>
#include <stdlib.h>
#include <stdio.h>

#ifndef STDIN_FILENO
#define STDIN_FILENO 0
#define STDOUT_FILENO 1
#define STDERR_FILENO 2
#endif

#ifndef F_OK
#define F_OK 0
#define X_OK 1
#define W_OK 2
#define R_OK 4
#endif

static inline int pipe(int pfds[2]) {
    return _pipe(pfds, 256, 0);
}

#ifndef getpid
#define getpid _getpid
#endif
#ifndef isatty
#define isatty _isatty
#endif
#ifndef read
#define read _read
#endif
#ifndef write
#define write _write
#endif
#ifndef close
#define close _close
#endif
#ifndef unlink
#define unlink _unlink
#endif
#ifndef rmdir
#define rmdir _rmdir
#endif
#ifndef chdir
#define chdir _chdir
#endif
#ifndef getcwd
#define getcwd _getcwd
#endif
#ifndef access
#define access _access
#endif
#ifndef dup
#define dup _dup
#endif
#ifndef dup2
#define dup2 _dup2
#endif
#ifndef lseek
#define lseek _lseek
#endif
#ifndef sleep
static inline unsigned sleep(unsigned seconds) {
    _sleep(seconds * 1000);
    return 0;
}
#endif
