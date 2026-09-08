function Assert-PsoRuntimeExecutionAllowed {
    [CmdletBinding()]
    param([switch]$AllowPerformanceExecution)
    if (-not $AllowPerformanceExecution) {
        throw 'PSO runtime execution is disabled by default. This historical entry can start Players, tracing or performance work. Use Tools/Invoke-PsoValidation.ps1 for compile/CPU checks. Pass -AllowPerformanceExecution only after new explicit user authorization for this run.'
    }
}
