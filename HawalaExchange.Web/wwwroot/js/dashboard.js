// ==========================================
// متغیرهای سراسری برای نگهداری نمونه‌های نمودار
// ==========================================
if (window.Chart) {
    window.Chart.defaults.locale = "en-US";
    window.Chart.register({
        id: 'hawalaDashboardLinePalette',
        beforeUpdate(chart) {
            if (chart.canvas.id !== 'dailyChart' || !chart.canvas.closest('.dashboard-reference')) return;
            const dataset = chart.data.datasets[0];
            if (!dataset) return;
            const dark = document.documentElement.dataset.bsTheme === 'dark';
            dataset.borderColor = dataset.pointBackgroundColor = dark ? '#6bbcf6' : '#1a5c8a';
            dataset.pointBorderColor = dark ? '#1b2735' : '#ffffff';
        }
    });
}

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
                        family: 'Vazirmatn, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'Vazirmatn, Tahoma, sans-serif'
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
                            family: 'Vazirmatn, Tahoma, sans-serif',
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
                            family: 'Vazirmatn, Tahoma, sans-serif',
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
                        family: 'Vazirmatn, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'Vazirmatn, Tahoma, sans-serif'
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
                            family: 'Vazirmatn, Tahoma, sans-serif',
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
                            family: 'Vazirmatn, Tahoma, sans-serif',
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
                backgroundColor: data.map((_, index) => colors[index] || `hsl(${(210 + index * 137.508) % 360}, 55%, 48%)`),
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
                        family: 'Vazirmatn, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'Vazirmatn, Tahoma, sans-serif'
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
        '#8ac7d9',
        '#d97706',
        '#7c3aed'
    ];

    miniChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: data.map((_, index) => colors[index % colors.length]),
                borderColor: data.map((_, index) => colors[index % colors.length]),
                borderWidth: 1.5,
                borderRadius: 4,
                barPercentage: 0.6
            }]
        },
        plugins: [{
            id: 'activityCounts',
            afterDatasetsDraw(chart) {
                const {ctx} = chart;
                const color = getComputedStyle(document.documentElement).getPropertyValue('--app-ink').trim() || '#172033';
                ctx.save();
                ctx.fillStyle = color;
                ctx.font = '10px Tahoma, sans-serif';
                ctx.textAlign = 'center';
                ctx.textBaseline = 'bottom';
                chart.getDatasetMeta(0).data.forEach((bar, index) => {
                    ctx.fillText(new Intl.NumberFormat('en-US').format(Number(chart.data.datasets[0].data[index]) || 0), bar.x, Math.max(chart.chartArea.top + 12, bar.y - 4));
                });
                ctx.restore();
            }
        }],
        options: {
            layout: { padding: { top: 18 } },
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
                        family: 'Vazirmatn, Tahoma, sans-serif'
                    },
                    bodyFont: {
                        family: 'Vazirmatn, Tahoma, sans-serif'
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
                            family: 'Vazirmatn, Tahoma, sans-serif',
                            size: 10
                        },
                        precision: 0,
                        maxTicksLimit: 5
                    }
                },
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        color: '#4a5a6e',
                        font: {
                            family: 'Vazirmatn, Tahoma, sans-serif',
                            size: 10
                        }
                    }
                }
            }
        }
    });
};
