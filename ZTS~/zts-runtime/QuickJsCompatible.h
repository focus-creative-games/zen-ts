#pragma once

/*
 * QuickJS API surface for zts-runtime.
 * Upstream sources live beside this tree at ../quickjs after Install.
 */

#include "../quickjs/quickjs.h"
#include "../quickjs/quickjs-libc.h"

#if defined(CONFIG_BIGNUM)
/* ZTS v1 does not use bigint as a CLR integer channel. */
#endif
