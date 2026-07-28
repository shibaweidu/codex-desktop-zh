#include "CLibProcBridge.h"

#include <libproc.h>
#include <string.h>

int cz_proc_start_time(pid_t pid, uint64_t *seconds, uint64_t *microseconds) {
    if (seconds == NULL || microseconds == NULL) return 0;
    struct proc_bsdinfo info;
    memset(&info, 0, sizeof(info));
    int size = proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &info, sizeof(info));
    if (size != (int)sizeof(info)) return 0;
    *seconds = info.pbi_start_tvsec;
    *microseconds = info.pbi_start_tvusec;
    return 1;
}

int32_t cz_proc_list_all_pids(int32_t *buffer, int32_t capacity) {
    if (buffer == NULL || capacity <= 0) return proc_listallpids(NULL, 0);
    return proc_listallpids(buffer, capacity * (int32_t)sizeof(int32_t));
}

int32_t cz_proc_pid_path(pid_t pid, char *buffer, uint32_t buffer_size) {
    return proc_pidpath(pid, buffer, buffer_size);
}

int32_t cz_proc_name(pid_t pid, char *buffer, uint32_t buffer_size) {
    return proc_name(pid, buffer, buffer_size);
}
