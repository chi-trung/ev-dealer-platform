// src/services/complaintService.js

import api from "./api";

// Đường dẫn cơ sở so với baseURL của api (API Gateway, đã bao gồm /api)
const BASE_URL = "/CustomerService/Complaints";

const complaintService = {
  /**
   * Tạo một khiếu nại mới.
   * @param {Object} complaintData - Dữ liệu khiếu nại cần tạo.
   * @returns {Promise<Object>} - Dữ liệu khiếu nại đã được tạo.
   */
  createComplaint: async (complaintData) => {
    try {
      // api đã giải mã response.data, nên kết quả chính là payload JSON như response.json() trước đây
      const data = await api.post(BASE_URL, complaintData);
      return data;
    } catch (error) {
      console.error("Lỗi khi tạo khiếu nại:", error);
      throw error;
    }
  },

  /**
   * Lấy tất cả khiếu nại.
   * @returns {Promise<Array<Object>>} - Danh sách tất cả khiếu nại.
   */
  getAllComplaints: async () => {
    try {
      const data = await api.get(BASE_URL);
      return data;
    } catch (error) {
      console.error("Lỗi khi lấy tất cả khiếu nại:", error);
      throw error;
    }
  },

  /**
   * Lấy chi tiết một khiếu nại theo ID.
   * @param {string} id - ID của khiếu nại.
   * @returns {Promise<Object>} - Dữ liệu khiếu nại.
   */
  getComplaintById: async (id) => {
    try {
      const data = await api.get(`${BASE_URL}/${id}`);
      return data;
    } catch (error) {
      console.error(`Lỗi khi lấy khiếu nại với ID ${id}:`, error);
      throw error;
    }
  },

  /**
   * Cập nhật một khiếu nại hiện có.
   * @param {string} id - ID của khiếu nại cần cập nhật.
   * @param {Object} updatedData - Dữ liệu cập nhật cho khiếu nại.
   * @returns {Promise<Object>} - Dữ liệu khiếu nại đã được cập nhật.
   */
  updateComplaint: async (id, updatedData) => {
    try {
      const data = await api.put(`${BASE_URL}/${id}`, updatedData);
      // Nếu backend trả về 204 No Content, axios giải mã thành chuỗi rỗng; giữ hành vi trả {} như cũ
      return data || {};
    } catch (error) {
      console.error(`Lỗi khi cập nhật khiếu nại với ID ${id}:`, error);
      throw error;
    }
  },

  /**
   * Xóa một khiếu nại.
   * @param {string} id - ID của khiếu nại cần xóa.
   * @returns {Promise<void>}
   */
  deleteComplaint: async (id) => {
    try {
      await api.delete(`${BASE_URL}/${id}`);
      // Không trả về dữ liệu cho thao tác xóa thành công
      return;
    } catch (error) {
      console.error(`Lỗi khi xóa khiếu nại với ID ${id}:`, error);
      throw error;
    }
  },
};

export { complaintService };
