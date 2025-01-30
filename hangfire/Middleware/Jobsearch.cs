using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;

namespace hangfire.Middleware
{
    public class JobSearchMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IRecurringJobManager _recurringJobManager;

        public JobSearchMiddleware(RequestDelegate next, IRecurringJobManager recurringJobManager)
        {
            _next = next;
            _recurringJobManager = recurringJobManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/hangfire/jobs/search"))
            {
                await HandleJobSearch(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments("/hangfire/jobs") && context.Request.Query["jobId"].Count > 0)
            {
                await HandleJobSearchJson(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments("/hangfire/jobs/delete"))
            {
                await HandleJobDelete(context);
                return;
            }

            await _next(context);
        }

        private async Task HandleJobSearch(HttpContext context)
        {
            var response = new StringBuilder();
            response.Append("<html><head>");
            response.Append("<meta charset='UTF-8'>");
            response.Append("<title>Hangfire Job Search</title>");
            response.Append("<style>");
            response.Append(@"
                body {
                    font-family: Arial, sans-serif;
                    margin: 0;
                    padding: 0;
                    box-sizing: border-box;
                }
                h1, h2 {
                    text-align: center;
                    margin: 20px 0;
                }
                .search-container {
                    display: flex;
                    justify-content: center;
                    margin-bottom: 20px;
                }
                #searchBox {
                    padding: 10px;
                    margin-right: 10px;
                    border: 1px solid #ccc;
                    border-radius: 4px;
                    width: 300px;
                }
                table {
                    width: 100%;
                    max-width: 1200px;
                    margin: 0 auto 20px;
                    border-collapse: collapse;
                }
                th, td {
                    text-align: left;
                    padding: 10px;
                    border: 1px solid #ddd;
                }
                th {
                    background-color: #f4f4f4;
                    position: sticky;
                    top: 0;
                    background: white;
                    z-index: 10;
                }
                tr:nth-child(even) {
                    background-color: #f9f9f9;
                }
                .no-results {
                    text-align: center;
                    color: #888;
                    padding: 20px;
                }
                #jobsTable {
                    display: none;
                }
                .delete-btn {
                    color: red;
                    cursor: pointer;
                    text-decoration: underline;
                }
                .modal {
                    display: none;
                    position: fixed;
                    z-index: 1000;
                    left: 0;
                    top: 0;
                    width: 100%;
                    height: 100%;
                    overflow: auto;
                    background-color: rgba(0,0,0,0.4);
                }
                .modal-content {
                    background-color: #fefefe;
                    margin: 15% auto;
                    padding: 20px;
                    border: 1px solid #888;
                    width: 300px;
                    text-align: center;
                }
                .modal-buttons {
                    margin-top: 20px;
                }
                .modal-buttons button {
                    margin: 0 10px;
                    padding: 10px 20px;
                }
            ");
            response.Append("</style>");
            response.Append("</head><body>");
            response.Append("<h1>Recurring Job Search</h1>");

            // Delete Confirmation Modal
            response.Append(@"
                <div id='deleteModal' class='modal'>
                    <div class='modal-content'>
                        <h2>Confirm Job Deletion</h2>
                        <p>Are you sure you want to delete the job?</p>
                        <div class='modal-buttons'>
                            <button id='confirmDelete'>Yes, Delete</button>
                            <button id='cancelDelete'>Cancel</button>
                        </div>
                    </div>
                </div>
            ");

            // Search container
            response.Append("<div class='search-container'>");
            response.Append("<input type='text' id='searchBox' placeholder='Search Job ID' autocomplete='off'/>");
            response.Append("</div>");

            // Table container
            response.Append("<div class='table-container'>");
            response.Append("<table id='jobsTable'>");
            response.Append("<thead>");
            response.Append("<tr><th>Job ID</th><th>Cron</th><th>Last Execution</th><th>Next Execution</th><th>Queue</th><th>Action</th></tr>");
            response.Append("</thead>");
            response.Append("<tbody id='jobTableBody'>");
            response.Append("</tbody>");
            response.Append("</table>");
            response.Append("<div id='noResultsMessage' class='no-results' style='display:block;'>Enter a search term to find jobs</div>");
            response.Append("</div>");

            // Updated JavaScript
            response.Append("<script>");
            response.Append(@"
document.addEventListener('DOMContentLoaded', function() {
    const searchBox = document.getElementById('searchBox');
    const jobTable = document.getElementById('jobsTable');
    const jobTableBody = document.getElementById('jobTableBody');
    const noResultsMessage = document.getElementById('noResultsMessage');
    const deleteModal = document.getElementById('deleteModal');
    const confirmDeleteBtn = document.getElementById('confirmDelete');
    const cancelDeleteBtn = document.getElementById('cancelDelete');

    let searchTimeout;
    let currentJobToDelete = null;

    // Event delegation for delete buttons
    jobTableBody.addEventListener('click', function(e) {
        if (e.target.classList.contains('delete-btn')) {
            currentJobToDelete = e.target.getAttribute('data-job-id');
            deleteModal.style.display = 'block';
        }
    });

    // Confirm delete
    confirmDeleteBtn.addEventListener('click', function() {
        if (currentJobToDelete) {
            fetch(`/hangfire/jobs/delete`, { 
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                },
                body: `jobId=${encodeURIComponent(currentJobToDelete)}`
            })
            .then(response => response.json())
            .then(result => {
                if (result.success) {
                    // Remove the deleted job row
                    const rowToRemove = document.querySelector(`tr[data-job-id='${currentJobToDelete}']`);
                    if (rowToRemove) {
                        rowToRemove.remove();
                        
                        // Check if any rows remain
                        if (jobTableBody.children.length === 0) {
                            jobTable.style.display = 'none';
                            noResultsMessage.textContent = 'No jobs found';
                            noResultsMessage.style.display = 'block';
                        }
                    }
                    deleteModal.style.display = 'none';
                    currentJobToDelete = null;
                } else {
                    alert('Failed to delete job: ' + result.message);
                }
            })
            .catch(error => {
                console.error('Error:', error);
                alert('An error occurred while deleting the job');
            });
        }
    });

    // Cancel delete
    cancelDeleteBtn.addEventListener('click', function() {
        deleteModal.style.display = 'none';
        currentJobToDelete = null;
    });

    // Close modal when clicking outside
    window.addEventListener('click', function(e) {
        if (e.target == deleteModal) {
            deleteModal.style.display = 'none';
            currentJobToDelete = null;
        }
    });

    searchBox.addEventListener('input', function() {
        // Clear previous timeout
        clearTimeout(searchTimeout);

        const query = this.value.toLowerCase().trim();

        // Clear previous results
        jobTableBody.innerHTML = '';
        jobTable.style.display = 'none';
        noResultsMessage.textContent = 'Enter a search term to find jobs';
        noResultsMessage.style.display = 'block';

        // Debounce search to prevent too many requests
        searchTimeout = setTimeout(() => {
            // Only search if query length is sufficient
            if (query.length >= 2) {
                // Fetch jobs based on search query
                fetch(`/hangfire/jobs?jobId=${encodeURIComponent(query)}`)
                    .then(response => response.json())
                    .then(jobs => {
                        if (jobs.length > 0) {
                            jobs.forEach(job => {
                                const row = `
                                    <tr data-job-id='${job.id}'>
                                        <td>${job.id}</td>
                                        <td>${job.cron || 'N/A'}</td>
                                        <td>${job.lastExecution || 'N/A'}</td>
                                        <td>${job.nextExecution || 'N/A'}</td>
                                        <td>${job.queue || 'N/A'}</td>
                                        <td><span class='delete-btn' data-job-id='${job.id}'>Delete</span></td>
                                    </tr>
                                `;
                                jobTableBody.innerHTML += row;
                            });
                            jobTable.style.display = 'table';
                            noResultsMessage.style.display = 'none';
                        } else {
                            noResultsMessage.textContent = 'No jobs found';
                            noResultsMessage.style.display = 'block';
                        }
                    })
                    .catch(error => {
                        console.error('Error:', error);
                        noResultsMessage.textContent = 'Error fetching jobs';
                        noResultsMessage.style.display = 'block';
                    });
            }
        }, 300); // 300ms debounce
    });
});
");
            response.Append("</script>");

            response.Append("</body></html>");

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(response.ToString());
        }

        private async Task HandleJobSearchJson(HttpContext context)
        {
            var jobIdFilter = context.Request.Query["jobId"].ToString();

            using (var connection = JobStorage.Current.GetConnection())
            {
                var recurringJobs = connection.GetRecurringJobs();

                // Filter jobs if jobId is provided
                if (!string.IsNullOrWhiteSpace(jobIdFilter))
                {
                    recurringJobs = recurringJobs
                        .Where(job => job.Id.Contains(jobIdFilter, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // Convert to anonymous type for JSON serialization
                var jobsToReturn = recurringJobs.Select(job => new
                {
                    id = job.Id,
                    cron = job.Cron,
                    lastExecution = job.LastExecution?.ToString() ?? "N/A",
                    nextExecution = job.NextExecution?.ToString() ?? "N/A",
                    queue = job.Queue
                });

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(jobsToReturn);
            }
        }

        private async Task HandleJobDelete(HttpContext context)
        {
            string jobId = null;

            // Try to get jobId from different possible sources
            if (context.Request.Query.ContainsKey("jobId"))
            {
                jobId = context.Request.Query["jobId"].ToString();
            }
            else if (context.Request.HasFormContentType)
            {
                var formCollection = await context.Request.ReadFormAsync();
                jobId = formCollection["jobId"].ToString();
            }

            if (string.IsNullOrWhiteSpace(jobId))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { success = false, message = "Job ID is required" });
                return;
            }

            try
            {
                // Check if the job exists
                using (var connection = JobStorage.Current.GetConnection())
                {
                    var recurringJob = connection.GetRecurringJobs()
                        .FirstOrDefault(j => j.Id == jobId);

                    if (recurringJob == null)
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsJsonAsync(new { success = false, message = "Job not found" });
                        return;
                    }
                }

                // Actually remove the recurring job
                _recurringJobManager.RemoveIfExists(jobId);

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { success = true });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { success = false, message = ex.Message });
            }
        }
    }
}