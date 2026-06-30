// ==========================================
// توابع نمودارها برای Dashboard
// ==========================================

// متغیرهای سراسری برای نگهداری نمونه‌های نمودار
let transactionChartInstance = null;
let statusChartInstance = null;
let currencyChartInstance = null;

// ==========================================
// رسم نمودار خطی (تراکنش‌های روزانه)
// ==========================================
window.renderTransactionChart = function (canvasId, labels, data, label) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // اگر نمودار قبلی وجود دارد، آن را نابود کن
    if (transactionChartInstance) {
        transactionChartInstance.destroy();
        transactionChartInstance = null;
    }

    const gradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 400);
    gradient.addColorStop(0, 'rgba(0, 97, 242, 0.3)');
    gradient.addColorStop(1, 'rgba(0, 97, 242, 0.0)');

    transactionChartInstance = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: label || 'تعداد تراکنش‌ها',
                data: data,
                borderColor: '#0061f2',
                backgroundColor: gradient,
                fill: true,
                tension: 0.4,
                pointBackgroundColor: '#0061f2',
                pointBorderColor: '#fff',
                pointBorderWidth: 2,
                pointRadius: 4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: true,
                    position: 'top',
                    labels: {
                        font: {
                            family: 'IRANSans, sans-serif',
                            size: 12
                        }
                    }
                },
                tooltip: {
                    backgroundColor: 'rgba(0,0,0,0.8)',
                    titleFont: {
                        family: 'IRANSans, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, sans-serif'
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0,0,0,0.05)'
                    },
                    ticks: {
                        stepSize: 1
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        font: {
                            family: 'IRANSans, sans-serif'
                        }
                    }
                }
            },
            interaction: {
                intersect: false,
                mode: 'index'
            }
        }
    });
};

// ==========================================
// رسم نمودار دایره‌ای (وضعیت تراکنش‌ها)
// ==========================================
window.renderStatusChart = function (canvasId, labels, data, colors) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (statusChartInstance) {
        statusChartInstance.destroy();
        statusChartInstance = null;
    }

    const defaultColors = ['#ffc107', '#28a745', '#dc3545', '#17a2b8', '#6c757d'];
    const chartColors = colors || defaultColors;

    statusChartInstance = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: chartColors.slice(0, data.length),
                borderColor: '#ffffff',
                borderWidth: 2,
                hoverOffset: 10
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: {
                        font: {
                            family: 'IRANSans, sans-serif',
                            size: 12
                        },
                        padding: 20,
                        usePointStyle: true,
                        pointStyle: 'circle'
                    }
                },
                tooltip: {
                    backgroundColor: 'rgba(0,0,0,0.8)',
                    titleFont: {
                        family: 'IRANSans, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, sans-serif'
                    },
                    callbacks: {
                        label: function (context) {
                            const total = context.dataset.data.reduce((a, b) => a + b, 0);
                            const percentage = ((context.parsed / total) * 100).toFixed(1);
                            return context.label + ': ' + context.parsed + ' (' + percentage + '%)';
                        }
                    }
                }
            },
            cutout: '65%'
        }
    });
};

// ==========================================
// رسم نمودار میله‌ای (ارزهای پرکاربرد)
// ==========================================
window.renderCurrencyChart = function (canvasId, labels, data, colors) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (currencyChartInstance) {
        currencyChartInstance.destroy();
        currencyChartInstance = null;
    }

    const defaultColors = [
        'rgba(0, 97, 242, 0.8)',
        'rgba(40, 167, 69, 0.8)',
        'rgba(255, 193, 7, 0.8)',
        'rgba(220, 53, 69, 0.8)',
        'rgba(23, 162, 184, 0.8)',
        'rgba(108, 117, 125, 0.8)'
    ];
    const chartColors = colors || defaultColors;

    currencyChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'تعداد تراکنش‌ها',
                data: data,
                backgroundColor: chartColors.slice(0, data.length),
                borderColor: chartColors.slice(0, data.length).map(c => c.replace('0.8', '1')),
                borderWidth: 1,
                borderRadius: 6,
                barPercentage: 0.6
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                },
                tooltip: {
                    backgroundColor: 'rgba(0,0,0,0.8)',
                    titleFont: {
                        family: 'IRANSans, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, sans-serif'
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0,0,0,0.05)'
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        font: {
                            family: 'IRANSans, sans-serif'
                        }
                    }
                }
            }
        }
    });
};