import api from "./api";

export const reportService = {
  getSummary: async (params = {}) => {
    try {
      const { from, to, type = "sales" } = params;
      const queryParams = new URLSearchParams();
      if (from) queryParams.append("from", from);
      if (to) queryParams.append("to", to);
      if (type) queryParams.append("type", type);

      const url = `/reports/summary${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return response.metrics || response;
    } catch (error) {
      console.error("Error fetching summary:", error);
      throw error;
    }
  },

  getSalesByRegion: async (params = {}) => {
    try {
      const { from, to } = params;
      const queryParams = new URLSearchParams();
      if (from) queryParams.append("from", from);
      if (to) queryParams.append("to", to);

      const url = `/reports/sales-by-region${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching sales by region:", error);
      throw error;
    }
  },

  getSalesProportion: async (params = {}) => {
    try {
      const { from, to } = params;
      const queryParams = new URLSearchParams();
      if (from) queryParams.append("from", from);
      if (to) queryParams.append("to", to);

      const url = `/reports/sales-proportion${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching sales proportion:", error);
      throw error;
    }
  },

  getTopVehicles: async (params = {}) => {
    try {
      const { from, to, limit = 5 } = params;
      const queryParams = new URLSearchParams();
      if (from) queryParams.append("from", from);
      if (to) queryParams.append("to", to);
      if (limit) queryParams.append("limit", limit.toString());

      const url = `/reports/top-vehicles${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching top vehicles:", error);
      throw error;
    }
  },

  getSalesSummary: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.fromDate) queryParams.append("fromDate", params.fromDate);
      if (params.toDate) queryParams.append("toDate", params.toDate);
      if (params.dealerId) queryParams.append("dealerId", params.dealerId);

      const url = `/reports/sales-summary${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return response;
    } catch (error) {
      console.error("Error fetching sales summary list:", error);
      throw error;
    }
  },

  getSalesSummaryById: async (id) => {
    if (!id) throw new Error("Sales summary id is required");
    return api.get(`/reports/sales-summary/${id}`);
  },

  createSalesSummary: async (payload) => {
    if (!payload) throw new Error("Payload is required");
    return api.post("/reports/sales-summary", payload);
  },

  getInventorySummary: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.dealerId) queryParams.append("dealerId", params.dealerId);
      if (params.vehicleId) queryParams.append("vehicleId", params.vehicleId);

      const url = `/reports/inventory-summary${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return response;
    } catch (error) {
      console.error("Error fetching inventory summary list:", error);
      throw error;
    }
  },

  getSalesByStaff: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.from) queryParams.append("from", params.from);
      if (params.to) queryParams.append("to", params.to);

      const url = `/reports/sales-by-staff${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching sales by staff:", error);
      throw error;
    }
  },

  getDebtReport: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.from) queryParams.append("from", params.from);
      if (params.to) queryParams.append("to", params.to);

      const url = `/reports/debt-report${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return response || { customers: [], manufacturers: [] };
    } catch (error) {
      console.error("Error fetching debt report:", error);
      throw error;
    }
  },

  getSalesByDealer: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.from) queryParams.append("from", params.from);
      if (params.to) queryParams.append("to", params.to);

      const url = `/reports/sales-by-dealer${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching sales by dealer:", error);
      throw error;
    }
  },

  getInventoryTrends: async (params = {}) => {
    try {
      const queryParams = new URLSearchParams();
      if (params.from) queryParams.append("from", params.from);
      if (params.to) queryParams.append("to", params.to);

      const url = `/reports/inventory-trends${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return Array.isArray(response) ? response : response.data || [];
    } catch (error) {
      console.error("Error fetching inventory trends:", error);
      throw error;
    }
  },

  getDemandForecast: async (params = {}) => {
    try {
      const { from, to } = params;
      const queryParams = new URLSearchParams();
      if (from) queryParams.append("from", from);
      if (to) queryParams.append("to", to);

      const url = `/reports/demand-forecast${
        queryParams.toString() ? "?" + queryParams.toString() : ""
      }`;
      const response = await api.get(url);
      return response;
    } catch (error) {
      console.error("Error fetching demand forecast:", error);
      throw error;
    }
  },

  createInventorySummary: async (payload) => {
    if (!payload) throw new Error("Payload is required");
    return api.post("/reports/inventory-summary", payload);
  },

  exportReport: async (payload = {}) => {
    try {
      // api's interceptor already unwraps to the body, so a blob call resolves to the Blob
      const blob = await api.post("/reports/export", payload, {
        responseType: "blob",
      });
      return blob;
    } catch (error) {
      console.error("Error exporting report:", error);
      throw error;
    }
  },
};

export default reportService;
