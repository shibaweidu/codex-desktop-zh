#ifndef C_LIBPROC_BRIDGE_H
#define C_LIBPROC_BRIDGE_H

#include <stdint.h>
#include <sys/types.h>

int cz_proc_start_time(pid_t pid, uint64_t *seconds, uint64_t *microseconds);
int32_t cz_proc_list_all_pids(int32_t *buffer, int32_t capacity);
int32_t cz_proc_pid_path(pid_t pid, char *buffer, uint32_t buffer_size);
int32_t cz_proc_name(pid_t pid, char *buffer, uint32_t buffer_size);

#endif
