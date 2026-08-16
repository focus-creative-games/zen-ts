/* <sys/time.h> shim for MSVC QuickJS builds (ZTS). */
#pragma once
#include <stdint.h>
#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

#ifndef _ZTS_SYS_TIME_H_
#define _ZTS_SYS_TIME_H_

#ifndef _TIMEVAL_DEFINED
#define _TIMEVAL_DEFINED
struct timeval {
    long tv_sec;
    long tv_usec;
};
#endif

#ifndef _TIMEZONE_DEFINED
#define _TIMEZONE_DEFINED
struct timezone {
    int tz_minuteswest;
    int tz_dsttime;
};
#endif

#ifndef gettimeofday
static inline int gettimeofday(struct timeval *tp, struct timezone *tz)
{
    (void)tz;
#ifdef _WIN32
    FILETIME ft;
    ULARGE_INTEGER uli;
    GetSystemTimeAsFileTime(&ft);
    uli.LowPart = ft.dwLowDateTime;
    uli.HighPart = ft.dwHighDateTime;
    const uint64_t EPOCH = 116444736000000000ULL;
    uint64_t t = (uli.QuadPart - EPOCH) / 10ULL;
    tp->tv_sec = (long)(t / 1000000ULL);
    tp->tv_usec = (long)(t % 1000000ULL);
#else
    (void)tp;
#endif
    return 0;
}
#endif

#endif
