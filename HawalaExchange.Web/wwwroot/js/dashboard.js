// ==========================================
// متغیرهای سراسری برای نگهداری نمونه‌های نمودار
// ==========================================
let dailyChartInstance = null;
let periodicChartInstance = null;
let liquidityChartInstance = null;
let miniChartInstance = null;   // ✅ اضافه شد

// ==========================================
// رسم نمودار خطی (تراکنش‌های روزانه) - تم روشن
// ==========================================
window.renderDailyChart = function (canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (dailyChartInstance) {
        dailyChartInstance.destroy();
        dailyChartInstance = null;
    }

    const gradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 200);
    gradient.addColorStop(0, 'rgba(26, 92, 138, 0.20)');
    gradient.addColorStop(1, 'rgba(26, 92, 138, 0.0)');

    dailyChartInstance = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'تعداد تراکنش',
                data: data,
                borderColor: '#1a5c8a',
                backgroundColor: gradient,
                fill: true,
                tension: 0.4,
                pointBackgroundColor: '#1a5c8a',
                pointBorderColor: '#ffffff',
                pointBorderWidth: 2,
                pointRadius: 4,
                pointHoverRadius: 6
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
                    backgroundColor: 'rgba(255,255,255,0.95)',
                    titleColor: '#1a3d5c',
                    bodyColor: '#1e293b',
                    borderColor: '#cbd5e1',
                    borderWidth: 1,
                    titleFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0,0,0,0.05)',
                        drawBorder: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
                        },
                        stepSize: 5
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
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
// رسم نمودار میله‌ای (گراف دوره‌ای مفاد) - تم روشن
// ==========================================
window.renderPeriodicChart = function (canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (periodicChartInstance) {
        periodicChartInstance.destroy();
        periodicChartInstance = null;
    }

    const colors = [
        '#1a5c8a',
        '#3a7ca8',
        '#5a9cc6',
        '#7abce4'
    ];

    periodicChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors.slice(0, data.length),
                borderColor: colors.slice(0, data.length),
                borderWidth: 1.5,
                borderRadius: 4
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
                    backgroundColor: 'rgba(255,255,255,0.95)',
                    titleColor: '#1a3d5c',
                    bodyColor: '#1e293b',
                    borderColor: '#cbd5e1',
                    borderWidth: 1,
                    titleFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0,0,0,0.05)',
                        drawBorder: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
                        },
                        stepSize: 20
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
                        }
                    }
                }
            }
        }
    });
};

// ==========================================
// رسم نمودار دایره‌ای کوچک (نقدینگی) - تم روشن
// ==========================================
window.renderLiquidityChart = function (canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (liquidityChartInstance) {
        liquidityChartInstance.destroy();
        liquidityChartInstance = null;
    }

    const colors = [
        '#1a5c8a',
        '#4a8db7',
        '#8ac7d9'
    ];

    liquidityChartInstance = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors.slice(0, data.length),
                borderColor: '#ffffff',
                borderWidth: 2,
                hoverOffset: 8
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
                    backgroundColor: 'rgba(255,255,255,0.95)',
                    titleColor: '#1a3d5c',
                    bodyColor: '#1e293b',
                    borderColor: '#cbd5e1',
                    borderWidth: 1,
                    titleFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    },
                    callbacks: {
                        label: function (context) {
                            const total = context.dataset.data.reduce((a, b) => a + b, 0);
                            const percentage = total > 0 ? ((context.parsed / total) * 100).toFixed(0) : 0;
                            return context.label + ': ' + percentage + '%';
                        }
                    }
                }
            },
            cutout: '70%'
        }
    });
};

// ==========================================
// رسم نمودار میله‌ای کوچک (معاملات) - تم روشن
// ==========================================
window.renderMiniChart = function (canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (miniChartInstance) {
        miniChartInstance.destroy();
        miniChartInstance = null;
    }

    const colors = [
        '#1a5c8a',
        '#4a8db7',
        '#8ac7d9'
    ];

    miniChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors.slice(0, data.length),
                borderColor: colors.slice(0, data.length).map(c => c),
                borderWidth: 1.5,
                borderRadius: 4,
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
                    backgroundColor: 'rgba(255,255,255,0.95)',
                    titleColor: '#1a3d5c',
                    bodyColor: '#1e293b',
                    borderColor: '#cbd5e1',
                    borderWidth: 1,
                    titleFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'IRANSans, Tahoma, sans-serif'
                    }
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0,0,0,0.05)',
                        drawBorder: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
                        },
                        stepSize: 10
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'IRANSans, Tahoma, sans-serif',
                            size: 10
                        }
                    }
                }
            }
        }
    });
};